// Minimal Windows desktop interface for live BLE heart rate.
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using System.Threading.Tasks;

using DokiDoki.Backend;

namespace DokiDoki.Frontend;

public sealed partial class HeartRateWindow : Window
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Stopwatch freshness = new Stopwatch();
    private readonly BleHeartRateService bluetooth = new();
    private int connectionVersion;
    private bool busy;
    private bool closeRequested;

    /// <summary>Initializes the WPF layout and wires the Bluetooth controls.</summary>
    public HeartRateWindow()
    {
        InitializeComponent();
        Closing += (sender, args) =>
        {
            if (!busy) return;
            closeRequested = true;
            args.Cancel = true;
            status.Text = "正在结束蓝牙操作…";
        };
        Closed += (sender, args) => Disconnect("");
        scan.Click += async (sender, args) => await ScanAsync();
        connect.Click += async (sender, args) =>
        {
            if (bluetooth.IsSubscribed) Disconnect("已断开");
            else if (devices.SelectedItem != null) await ConnectAsync((BleHeartRateService.Candidate)devices.SelectedItem);
            RefreshButtons();
        };
        devices.SelectionChanged += (sender, args) => RefreshButtons();
        timer.Tick += (sender, args) =>
        {
            if (!bluetooth.IsSubscribed) return;
            if (bluetooth.IsDisconnected)
                Disconnect("手环已断开");
            else if (freshness.Elapsed.TotalSeconds >= 15)
                Disconnect("心率数据已中断，请检查手环广播");
            RefreshButtons();
        };
    }

    /// <summary>Reflects scan and connection state in the available controls.</summary>
    private void RefreshButtons()
    {
        scan.IsEnabled = devices.IsEnabled = !busy && !bluetooth.IsSubscribed;
        connect.IsEnabled = !busy && (bluetooth.IsSubscribed || devices.SelectedItem != null);
        connect.Content = !bluetooth.IsSubscribed ? "连接" : "断开";
    }

    /// <summary>Collects advertisements and orders heart-rate candidates first.</summary>
    /// <returns>A task that completes once the device list is refreshed.</returns>
    private async Task ScanAsync()
    {
        busy = true;
        RefreshButtons();
        devices.Items.Clear();
        status.Text = "正在扫描附近蓝牙设备…";
        try
        {
            var results = await BleHeartRateService.ScanAsync();
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
    private async Task ConnectAsync(BleHeartRateService.Candidate candidate)
    {
        busy = true;
        RefreshButtons();
        status.Text = "正在连接手环…";
        try
        {
            var version = ++connectionVersion;
            await Task.Run(() => bluetooth.SubscribeAsync(candidate, bpm => ShowHeartRate(bpm, version)));
            if (!freshness.IsRunning) status.Text = "已连接，等待心率";
            freshness.Restart();
            timer.Start();
        }
        catch (Exception error) { Disconnect("连接失败：" + error.Message); }
        finally { FinishOperation(); }
    }

    /// <summary>Posts a heart-rate reading to the WPF thread.</summary>
    /// <param name="bpm">The decoded heart rate.</param>
    /// <param name="version">The connection that produced the reading.</param>
    private void ShowHeartRate(int bpm, int version)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (connectionVersion != version) return;
            freshness.Restart();
            value.Text = bpm + " BPM";
            status.Text = "正在接收心率";
        });
    }

    /// <summary>Stops notifications, releases the connection and clears the old reading.</summary>
    /// <param name="message">The status to show after cleanup.</param>
    private void Disconnect(string message)
    {
        timer.Stop();
        freshness.Reset();
        connectionVersion++;
        bluetooth.Dispose();
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

}
