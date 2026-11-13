// Console probe for validating standard BLE heart-rate notifications on Windows.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using Windows.Devices.Enumeration;

internal static class HeartRateProbe
{
    private static readonly ManualResetEventSlim Stop = new ManualResetEventSlim();
    private static readonly object Gate = new object();
    private static DateTime lastReading;
    private static readonly Dictionary<ulong, short> SignalStrengths = new Dictionary<ulong, short>();
    private static readonly Dictionary<ulong, BluetoothAddressType> AddressTypes = new Dictionary<ulong, BluetoothAddressType>();

    /// <summary>Runs the scan and subscription, and converts failures to a process exit code.</summary>
    /// <returns>Zero on success; one if validation could not be completed.</returns>
    private static int Main()
    {
        Console.CancelKeyPress += (sender, args) => { args.Cancel = true; Stop.Set(); };
        try { return RunAsync().GetAwaiter().GetResult(); }
        catch (Exception error)
        {
            Console.Error.WriteLine("{0} (0x{1:X8})", error.Message, error.HResult);
            return 1;
        }
    }

    /// <summary>Collects nearby device names during an active scan.</summary>
    /// <returns>A snapshot keyed by Bluetooth address.</returns>
    private static Dictionary<ulong, string> Scan()
    {
        var devices = new Dictionary<ulong, string>();
        var watcher = new BluetoothLEAdvertisementWatcher();
        var scanGate = new object();
        BluetoothError failure = BluetoothError.Success;
        watcher.ScanningMode = BluetoothLEScanningMode.Active;
        var stopped = new TaskCompletionSource<BluetoothError>();
        var heartRateDevices = new HashSet<ulong>();
        bool collecting = true;
        watcher.Received += (sender, args) =>
        {
            lock (scanGate)
            {
                if (!collecting) return;
                foreach (var uuid in args.Advertisement.ServiceUuids)
                    if (uuid == GattServiceUuids.HeartRate) heartRateDevices.Add(args.BluetoothAddress);
                AddressTypes[args.BluetoothAddress] = args.BluetoothAddressType;
                SignalStrengths[args.BluetoothAddress] = args.RawSignalStrengthInDBm;
                var name = args.Advertisement.LocalName;
                if (!devices.ContainsKey(args.BluetoothAddress) || !String.IsNullOrEmpty(name))
                    devices[args.BluetoothAddress] = name;
            }
        };
        watcher.Stopped += (sender, args) =>
        {
            stopped.TrySetResult(args.Error);
        };
        Console.WriteLine("Scanning for BLE devices for 10 seconds...");
        watcher.Start();
        try
        {
            for (int i = 0; i < 40 && watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started; i++)
                if (Stop.Wait(250)) break;
        }
        finally { watcher.Stop(); }
        if (stopped.Task.Wait(1500)) failure = stopped.Task.Result;

        lock (scanGate)
        {
            collecting = false;
            if (failure != BluetoothError.Success)
                throw new InvalidOperationException("BLE scan stopped: " + failure);
            var snapshot = new Dictionary<ulong, string>(devices);
            foreach (var address in heartRateDevices) snapshot[address] = "[HR 180D] " + snapshot[address];
            return snapshot;
        }
    }

    /// <summary>Lets the user select a device and checks for a sustained stream of heart-rate readings.</summary>
    /// <returns>Zero after at least two valid readings, or one if the stream cannot be validated.</returns>
    private static async Task<int> RunAsync()
    {
        var devices = new List<KeyValuePair<ulong, string>>(Scan());
        if (Stop.IsSet) return 1;
        if (devices.Count == 0)
            throw new InvalidOperationException("No BLE advertisements received. Check Windows Bluetooth and enable heart-rate broadcast on the band.");
        devices.Sort((left, right) => SignalStrengths[right.Key].CompareTo(SignalStrengths[left.Key]));
        for (int index = 0; index < devices.Count; index++)
        {
            var item = devices[index];
            Console.WriteLine("{0,2}. {1} [{2:X12}] {3} dBm", index + 1,
                String.IsNullOrEmpty(item.Value) ? "(unnamed)" : item.Value, item.Key, SignalStrengths[item.Key]);
        }
        int advertised = devices.FindAll(item => item.Value != null && item.Value.StartsWith("[HR 180D] ", StringComparison.Ordinal)).Count;
        if (advertised == 1)
            Console.WriteLine("One device advertises the Heart Rate Service.");
        Console.Write("Device number (Enter to select the sole HR device or exit): ");
        int selection;
        var input = Console.ReadLine();
        if (String.IsNullOrWhiteSpace(input) && advertised == 1)
            input = (devices.FindIndex(item => item.Value != null && item.Value.StartsWith("[HR 180D] ", StringComparison.Ordinal)) + 1).ToString();
        if (String.IsNullOrWhiteSpace(input)) return 1;
        if (!Int32.TryParse(input, out selection) || selection < 1 || selection > devices.Count)
            throw new InvalidOperationException("Invalid device number.");

        var address = devices[selection - 1].Key;
        var discovered = await WaitForAsync(BluetoothLEDevice.FromBluetoothAddressAsync(address, AddressTypes[address]));
        if (discovered == null)
            discovered = await WaitForAsync(BluetoothLEDevice.FromBluetoothAddressAsync(address));
        if (discovered == null)
        {
            var matches = await WaitForAsync(DeviceInformation.FindAllAsync(
                BluetoothLEDevice.GetDeviceSelectorFromBluetoothAddress(address)));
            foreach (var match in matches)
            {
                discovered = await WaitForAsync(BluetoothLEDevice.FromIdAsync(match.Id));
                if (discovered != null) break;
            }
        }
        using (var device = discovered)
        {
            if (device == null) throw new InvalidOperationException("Windows could not open the selected device.");
            Console.WriteLine("Discovering Heart Rate Service (180D)...");
            var services = await WaitForAsync(device.GetGattServicesForUuidAsync(GattServiceUuids.HeartRate, BluetoothCacheMode.Uncached));
            RequireSuccess(services.Status, "Heart Rate Service discovery");
            if (services.Services.Count == 0)
                throw new InvalidOperationException("Heart Rate Service (180D) missing. Check the selected device and broadcast mode.");
            try
            {
                var result = await WaitForAsync(services.Services[0].GetCharacteristicsForUuidAsync(
                    GattCharacteristicUuids.HeartRateMeasurement, BluetoothCacheMode.Uncached));
                RequireSuccess(result.Status, "Heart Rate Measurement discovery");
                if (result.Characteristics.Count == 0)
                    throw new InvalidOperationException("Heart Rate Measurement (2A37) missing.");

                var characteristic = result.Characteristics[0];
                if ((characteristic.CharacteristicProperties & GattCharacteristicProperties.Notify) == 0)
                    throw new InvalidOperationException("Heart Rate Measurement does not support Notify.");
                int readings = 0;
                bool subscribed = false;
                lastReading = DateTime.UtcNow;
                Windows.Foundation.TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> handler =
                    (sender, args) =>
                    {
                        using (var reader = DataReader.FromBuffer(args.CharacteristicValue))
                        {
                            var bytes = new byte[reader.UnconsumedBufferLength];
                            reader.ReadBytes(bytes);
                            int bpm;
                            if (!TryDecode(bytes, out bpm)) return;
                            lock (Gate)
                            {
                                lastReading = DateTime.UtcNow;
                                readings++;
                                Console.WriteLine("{0:HH:mm:ss}  {1} BPM", DateTime.Now, bpm);
                            }
                        }
                    };
                characteristic.ValueChanged += handler;
                try
                {
                    RequireSuccess(await WaitForAsync(characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.Notify)), "CCCD Notify subscription");
                    subscribed = true;
                    lock (Gate) { lastReading = DateTime.UtcNow; }
                    Console.WriteLine("Subscribed to 2A37. Press Ctrl+C to finish.");
                    while (!Stop.Wait(250))
                    {
                        lock (Gate)
                        {
                            if (DateTime.UtcNow - lastReading < TimeSpan.FromSeconds(15)) continue;
                            Console.Error.WriteLine("No valid reading for 15 seconds. Received {0} reading(s).", readings);
                            return 1;
                        }
                    }
                    lock (Gate)
                    {
                        Console.WriteLine("Received {0} valid reading(s).", readings);
                        return readings >= 2 ? 0 : 1;
                    }
                }
                finally
                {
                    characteristic.ValueChanged -= handler;
                    if (subscribed && device.ConnectionStatus == BluetoothConnectionStatus.Connected)
                        WaitForAsync(characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None)).GetAwaiter().GetResult();
                }
            }
            finally
            {
                foreach (var service in services.Services) service.Dispose();
            }
        }
    }

    /// <summary>Waits for a Windows operation using the metadata available on the test PC.</summary>
    /// <typeparam name="T">Operation result type.</typeparam>
    /// <param name="operation">Started Windows Runtime operation.</param>
    /// <returns>The completed operation result.</returns>
    internal static async Task<T> WaitForAsync<T>(Windows.Foundation.IAsyncOperation<T> operation)
    {
        while (operation.Status == Windows.Foundation.AsyncStatus.Started)
            await Task.Delay(100);
        return operation.GetResults();
    }

    /// <summary>Checks whether a named GATT operation succeeded.</summary>
    /// <param name="status">Communication status returned by Windows.</param>
    /// <param name="operation">Operation to identify in the error message.</param>
    private static void RequireSuccess(GattCommunicationStatus status, string operation)
    {
        if (status != GattCommunicationStatus.Success)
            throw new InvalidOperationException(operation + " failed: " + status);
    }

    /// <summary>Reads the mandatory BPM field from a BLE Heart Rate Measurement packet.</summary>
    /// <param name="bytes">Packet starting with the Heart Rate Measurement flags.</param>
    /// <param name="bpm">Decoded beats per minute when the packet contains a usable reading.</param>
    /// <returns>True when the mandatory field is present and the sensor has not reported losing contact.</returns>
    internal static bool TryDecode(byte[] bytes, out int bpm)
    {
        bpm = 0;
        if (bytes.Length < 2) return false;
        var flags = bytes[0];
        if ((flags & 6) == 4) return false;
        if ((flags & 1) == 0) bpm = bytes[1];
        else if (bytes.Length >= 3) bpm = bytes[1] | (bytes[2] << 8);
        return bpm > 0;
    }
}
