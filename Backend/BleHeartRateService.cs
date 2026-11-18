// Windows BLE discovery and heart-rate subscription, independent of WPF.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace DokiDoki.Backend;

/// <summary>Owns the Windows Bluetooth resources for a single heart-rate connection.</summary>
internal sealed class BleHeartRateService : IDisposable
{
    private BluetoothLEDevice device;
    private GattDeviceService service;
    private GattCharacteristic measurement;
    private Windows.Foundation.TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> handler;

    internal bool IsSubscribed => measurement != null;
    internal bool IsDisconnected => device?.ConnectionStatus == BluetoothConnectionStatus.Disconnected;

    /// <summary>Stores the discovery information needed to open a selected device.</summary>
    internal sealed class Candidate
    {
        internal ulong Address;
        internal BluetoothAddressType AddressType;
        internal string Name;
        internal bool HeartRate;

        /// <summary>Formats the entry in the device list.</summary>
        /// <returns>The device name, address suffix and heart-rate indicator.</returns>
        public override string ToString()
        {
            return String.Format("{0}{1} · {2:X4}", HeartRate ? "♥  " : "",
                String.IsNullOrWhiteSpace(Name) ? "未命名设备" : Name, Address & 0xFFFF);
        }
    }

    /// <summary>Scans for nearby BLE devices and sorts heart-rate candidates first.</summary>
    /// <param name="cancellationToken">Stops the discovery session.</param>
    /// <param name="scanMilliseconds">Time spent collecting advertisements for this scan.</param>
    /// <returns>A snapshot of discovered devices.</returns>
    internal static async Task<List<Candidate>> ScanAsync(CancellationToken cancellationToken = default, int scanMilliseconds = 8000)
    {
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        var seen = new Dictionary<ulong, Candidate>();
        var stopped = new TaskCompletionSource<BluetoothError>(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Stopped += (sender, args) => stopped.TrySetResult(args.Error);
        watcher.Received += (sender, args) =>
        {
            lock (seen)
            {
                Candidate candidate;
                if (!seen.TryGetValue(args.BluetoothAddress, out candidate))
                {
                    candidate = new Candidate { Address = args.BluetoothAddress, AddressType = args.BluetoothAddressType };
                    seen.Add(candidate.Address, candidate);
                }
                if (!String.IsNullOrWhiteSpace(args.Advertisement.LocalName))
                    candidate.Name = args.Advertisement.LocalName;
                foreach (var uuid in args.Advertisement.ServiceUuids)
                    candidate.HeartRate |= uuid == GattServiceUuids.HeartRate;
            }
        };
        try
        {
            watcher.Start();
            await Task.WhenAny(stopped.Task, Task.Delay(scanMilliseconds, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            watcher.Stop();
            await Task.WhenAny(stopped.Task, Task.Delay(500));
            if (stopped.Task.IsCompleted && stopped.Task.Result != BluetoothError.Success)
                throw new InvalidOperationException(stopped.Task.Result.ToString());

            var results = new List<Candidate>();
            lock (seen)
            {
                foreach (var item in seen.Values)
                    results.Add(new Candidate { Address = item.Address, AddressType = item.AddressType,
                        Name = item.Name, HeartRate = item.HeartRate });
            }
            results.Sort((left, right) =>
            {
                int priority = right.HeartRate.CompareTo(left.HeartRate);
                return priority != 0 ? priority : String.Compare(left.Name, right.Name, StringComparison.CurrentCulture);
            });
            return results;
        }
        finally
        {
            if (watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started) watcher.Stop();
            // Let Windows stop the cancelled watcher before another scan starts.
            if (cancellationToken.IsCancellationRequested && watcher.Status == BluetoothLEAdvertisementWatcherStatus.Stopping)
                await Task.WhenAny(stopped.Task, Task.Delay(500));
        }
    }

    /// <summary>Performs GATT discovery and subscription on a worker thread.</summary>
    /// <param name="candidate">The selected BLE advertisement.</param>
    /// <param name="onReading">Receives each valid BPM on the Bluetooth callback thread.</param>
    /// <param name="cancellationToken">Stops the Windows Bluetooth operations.</param>
    /// <returns>A task that completes when notifications are enabled.</returns>
    internal async Task SubscribeAsync(Candidate candidate, Action<int> onReading, CancellationToken cancellationToken = default)
    {
        // Forward cancellation to Windows so the reconnect deadline also bounds GATT calls.
        Task<T> Await<T>(Windows.Foundation.IAsyncOperation<T> operation) => operation.AsTask(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (candidate.AddressType != BluetoothAddressType.Unspecified)
            device = await Await(BluetoothLEDevice.FromBluetoothAddressAsync(candidate.Address, candidate.AddressType));
        if (device == null)
            device = await Await(BluetoothLEDevice.FromBluetoothAddressAsync(candidate.Address));
        if (device == null)
        {
            var matches = await Await(DeviceInformation.FindAllAsync(
                BluetoothLEDevice.GetDeviceSelectorFromBluetoothAddress(candidate.Address)));
            foreach (var match in matches)
            {
                device = await Await(BluetoothLEDevice.FromIdAsync(match.Id));
                if (device != null) break;
            }
        }
        if (device == null) throw new InvalidOperationException("无法打开设备，请重新扫描");

        var services = await Await(device.GetGattServicesForUuidAsync(GattServiceUuids.HeartRate, BluetoothCacheMode.Uncached));
        foreach (var item in services.Services)
        {
            if (service == null) service = item;
            else item.Dispose();
        }
        if (services.Status != GattCommunicationStatus.Success || service == null)
            throw new InvalidOperationException("无法读取心率服务：" + services.Status);
        var result = await Await(service.GetCharacteristicsForUuidAsync(
            GattCharacteristicUuids.HeartRateMeasurement, BluetoothCacheMode.Uncached));
        if (result.Status == GattCommunicationStatus.AccessDenied)
        {
            var access = await Await(service.RequestAccessAsync());
            if (access == DeviceAccessStatus.Allowed)
                result = await Await(service.GetCharacteristicsForUuidAsync(
                    GattCharacteristicUuids.HeartRateMeasurement, BluetoothCacheMode.Uncached));
        }
        if (result.Status != GattCommunicationStatus.Success || result.Characteristics.Count == 0)
            throw new InvalidOperationException("无法读取心率特征：" + result.Status);
        measurement = result.Characteristics[0];
        if ((measurement.CharacteristicProperties & GattCharacteristicProperties.Notify) == 0)
            throw new InvalidOperationException("该设备未提供心率通知");
        handler = (sender, args) => ReceiveHeartRate(args, onReading);
        measurement.ValueChanged += handler;

        var subscribed = await Await(measurement.WriteClientCharacteristicConfigurationDescriptorAsync(
            GattClientCharacteristicConfigurationDescriptorValue.Notify));
        if (subscribed != GattCommunicationStatus.Success)
            throw new InvalidOperationException("无法订阅心率：" + subscribed);
    }

    /// <summary>Decodes a notification and forwards its BPM to the subscriber.</summary>
    /// <param name="onReading">Receives each valid BPM.</param>
    /// <param name="args">The incoming Bluetooth packet.</param>
    private static void ReceiveHeartRate(GattValueChangedEventArgs args, Action<int> onReading)
    {
        int bpm;
        using (var reader = DataReader.FromBuffer(args.CharacteristicValue))
        {
            var bytes = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(bytes);
            if (!BleMeasurement.TryDecode(bytes, out bpm)) return;
        }
        onReading(bpm);
    }

    /// <summary>Stops forwarding notifications and releases the active GATT connection.</summary>
    public void Dispose()
    {
        if (measurement != null && handler != null) measurement.ValueChanged -= handler;
        measurement = null;
        handler = null;
        if (service != null) service.Dispose();
        if (device != null) device.Dispose();
        service = null;
        device = null;
    }
}
