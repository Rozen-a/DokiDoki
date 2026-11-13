// Minimal Windows desktop interface for live BLE heart rate.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Size = System.Drawing.Size;
using Windows.Storage.Streams;

internal sealed class HeartRateWindow : Form
{
    private readonly ComboBox devices = new ComboBox();
    private readonly Button scan = new Button { Text = "扫描" };
    private readonly Button connect = new Button { Text = "连接", Enabled = false };
    private readonly Label value = new Label { Text = "— BPM", TextAlign = ContentAlignment.MiddleCenter };
    private readonly Label status = new Label { Text = "请在手环上开启心率广播", TextAlign = ContentAlignment.MiddleCenter };
    private readonly Timer timer = new Timer { Interval = 500 };
    private readonly Stopwatch freshness = new Stopwatch();
    private BluetoothLEDevice device;
    private GattDeviceService service;
    private GattCharacteristic measurement;
    private Windows.Foundation.TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> handler;
    private bool busy;
    private bool closeRequested;

    /// <summary>Stores the discovery information needed to open a selected device.</summary>
    private sealed class Candidate
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

    /// <summary>Starts the application in a single desktop window.</summary>
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new HeartRateWindow());
    }

    /// <summary>Builds the window and wires its scan, connection and timeout controls.</summary>
    private HeartRateWindow()
    {
        Text = "手环心率";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(480, 245);
        MinimumSize = new Size(460, 260);
        AutoScaleMode = AutoScaleMode.Dpi;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16),
            ColumnCount = 3, RowCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        devices.DropDownStyle = ComboBoxStyle.DropDownList;
        devices.AccessibleName = "蓝牙设备";
        devices.Dock = scan.Dock = connect.Dock = value.Dock = status.Dock = DockStyle.Fill;
        value.Font = new Font(Font.FontFamily, 48);
        layout.Controls.Add(devices, 0, 0);
        layout.Controls.Add(scan, 1, 0);
        layout.Controls.Add(connect, 2, 0);
        layout.Controls.Add(value, 0, 1);
        layout.SetColumnSpan(value, 3);
        layout.Controls.Add(status, 0, 2);
        layout.SetColumnSpan(status, 3);
        Controls.Add(layout);
        scan.Click += async (sender, args) => await ScanAsync();
        connect.Click += async (sender, args) =>
        {
            if (measurement != null) Disconnect("已断开");
            else if (devices.SelectedItem != null) await ConnectAsync((Candidate)devices.SelectedItem);
            RefreshButtons();
        };
        devices.SelectedIndexChanged += (sender, args) => RefreshButtons();
        timer.Tick += (sender, args) =>
        {
            if (measurement == null) return;
            if (device.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
                Disconnect("手环已断开");
            else if (freshness.Elapsed.TotalSeconds >= 15)
                Disconnect("心率数据已中断，请检查手环广播");
            RefreshButtons();
        };
    }

    /// <summary>Reflects scan and connection state in the available controls.</summary>
    private void RefreshButtons()
    {
        scan.Enabled = devices.Enabled = !busy && measurement == null;
        connect.Enabled = !busy && (measurement != null || devices.SelectedItem != null);
        connect.Text = measurement == null ? "连接" : "断开";
    }

    /// <summary>Collects advertisements and orders heart-rate candidates first.</summary>
    /// <returns>A task that completes once the device list is refreshed.</returns>
    private async Task ScanAsync()
    {
        busy = true;
        RefreshButtons();
        devices.Items.Clear();
        status.Text = "正在扫描附近蓝牙设备…";
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        var seen = new Dictionary<ulong, Candidate>();
        var stopped = new TaskCompletionSource<BluetoothError>();
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
            await Task.WhenAny(stopped.Task, Task.Delay(8000));
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
            foreach (var item in results) devices.Items.Add(item);
            int huawei = results.FindIndex(item => item.HeartRate && item.Name != null &&
                item.Name.StartsWith("HUAWEI Band HR", StringComparison.OrdinalIgnoreCase));
            if (huawei >= 0) devices.SelectedIndex = huawei;
            status.Text = results.Count > 0 ? "请选择手环并连接" : "未发现设备，请确认电脑蓝牙和手环广播已开启";
        }
        catch (Exception error) { status.Text = "扫描失败：" + error.Message; }
        finally { FinishOperation(); }
    }

    /// <summary>Opens the selected BLE device and subscribes to its heart-rate notifications.</summary>
    /// <param name="candidate">The advertisement selected by the user.</param>
    /// <returns>A task that ends when subscription succeeds or the connection attempt is cleaned up.</returns>
    private async Task ConnectAsync(Candidate candidate)
    {
        busy = true;
        RefreshButtons();
        status.Text = "正在连接手环…";
        try
        {
            await Task.Run(() => SubscribeAsync(candidate));
            if (!freshness.IsRunning) status.Text = "已连接，等待心率";
            freshness.Restart();
            timer.Start();
        }
        catch (Exception error) { Disconnect("连接失败：" + error.Message); }
        finally { FinishOperation(); }
    }

    /// <summary>Performs GATT discovery and subscription on a worker thread.</summary>
    /// <param name="candidate">The selected BLE advertisement.</param>
    /// <returns>A task that completes when notifications are enabled.</returns>
    private async Task SubscribeAsync(Candidate candidate)
    {
        device = await HeartRateProbe.WaitForAsync(BluetoothLEDevice.FromBluetoothAddressAsync(candidate.Address, candidate.AddressType));
        if (device == null)
            device = await HeartRateProbe.WaitForAsync(BluetoothLEDevice.FromBluetoothAddressAsync(candidate.Address));
        if (device == null)
        {
            var matches = await HeartRateProbe.WaitForAsync(DeviceInformation.FindAllAsync(
                BluetoothLEDevice.GetDeviceSelectorFromBluetoothAddress(candidate.Address)));
            foreach (var match in matches)
            {
                device = await HeartRateProbe.WaitForAsync(BluetoothLEDevice.FromIdAsync(match.Id));
                if (device != null) break;
            }
        }
        if (device == null) throw new InvalidOperationException("无法打开设备，请重新扫描");

        var services = await HeartRateProbe.WaitForAsync(device.GetGattServicesForUuidAsync(GattServiceUuids.HeartRate, BluetoothCacheMode.Uncached));
        foreach (var item in services.Services)
        {
            if (service == null) service = item;
            else item.Dispose();
        }
        if (services.Status != GattCommunicationStatus.Success || service == null)
            throw new InvalidOperationException("无法读取心率服务：" + services.Status);
        var result = await HeartRateProbe.WaitForAsync(service.GetCharacteristicsForUuidAsync(
            GattCharacteristicUuids.HeartRateMeasurement, BluetoothCacheMode.Uncached));
        if (result.Status == GattCommunicationStatus.AccessDenied)
        {
            var access = await HeartRateProbe.WaitForAsync(service.RequestAccessAsync());
            if (access == DeviceAccessStatus.Allowed)
                result = await HeartRateProbe.WaitForAsync(service.GetCharacteristicsForUuidAsync(
                    GattCharacteristicUuids.HeartRateMeasurement, BluetoothCacheMode.Uncached));
        }
        if (result.Status != GattCommunicationStatus.Success || result.Characteristics.Count == 0)
            throw new InvalidOperationException("无法读取心率特征：" + result.Status);
        measurement = result.Characteristics[0];
        if ((measurement.CharacteristicProperties & GattCharacteristicProperties.Notify) == 0)
            throw new InvalidOperationException("该设备未提供心率通知");
        handler = ReceiveHeartRate;
        measurement.ValueChanged += handler;

        var subscribed = await HeartRateProbe.WaitForAsync(measurement.WriteClientCharacteristicConfigurationDescriptorAsync(
            GattClientCharacteristicConfigurationDescriptorValue.Notify));
        if (subscribed != GattCommunicationStatus.Success)
            throw new InvalidOperationException("无法订阅心率：" + subscribed);
    }

    /// <summary>Decodes a notification and delivers it to the window thread.</summary>
    /// <param name="sender">The characteristic that sent the notification.</param>
    /// <param name="args">The incoming Bluetooth packet.</param>
    private void ReceiveHeartRate(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        int bpm;
        using (var reader = DataReader.FromBuffer(args.CharacteristicValue))
        {
            var bytes = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(bytes);
            if (!HeartRateProbe.TryDecode(bytes, out bpm)) return;
        }
        try
        {
            BeginInvoke((Action)(() =>
            {
                if (measurement != sender) return;
                freshness.Restart();
                value.Text = bpm + " BPM";
                status.Text = "正在接收心率";
            }));
        }
        catch (InvalidOperationException) { /* The window closed before delivery. */ }
    }

    /// <summary>Stops notifications, releases the connection and clears the old reading.</summary>
    /// <param name="message">The status to show after cleanup.</param>
    private void Disconnect(string message)
    {
        timer.Stop();
        freshness.Reset();
        if (measurement != null && handler != null) measurement.ValueChanged -= handler;
        measurement = null;
        handler = null;
        if (service != null) service.Dispose();
        if (device != null) device.Dispose();
        service = null;
        device = null;
        value.Text = "— BPM";
        status.Text = message;
    }

    /// <summary>Restores the controls and honors a close request made during a Bluetooth operation.</summary>
    private void FinishOperation()
    {
        busy = false;
        if (closeRequested) Close();
        else RefreshButtons();
    }

    /// <summary>Releases the Bluetooth connection before the window closes.</summary>
    /// <param name="args">The window close event.</param>
    protected override void OnFormClosing(FormClosingEventArgs args)
    {
        // Keep the window available until the active Windows operation has returned.
        if (busy && args.CloseReason == CloseReason.UserClosing)
        {
            closeRequested = true;
            args.Cancel = true;
            status.Text = "正在结束蓝牙操作…";
        }
        else { Disconnect(""); timer.Dispose(); }
        base.OnFormClosing(args);
    }
}
