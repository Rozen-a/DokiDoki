// Coordinates the dashboard, device picker, and one live BLE session.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DokiDoki.Backend;

namespace DokiDoki.Frontend;

/// <summary>Displays device connection state, current BPM, and the rolling heart-rate trend.</summary>
public sealed partial class HeartRateWindow : Window
{
    private HeartRateOverlay overlay;
    private OverlaySettingsWindow overlaySettings;
    private OverlayOptions overlayOptions = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer toastTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Queue<(double Time, int Bpm)> history = new();
    private readonly BleHeartRateService bluetooth = new();
    private double lastReading;
    private int connectionVersion;
    private bool busy;
    private bool connected;
    private bool closeRequested;

    /// <summary>Initializes both views and links timers to the connection lifecycle.</summary>
    public HeartRateWindow()
    {
        InitializeComponent();
        Closing += (_, args) =>
        {
            if (!busy) return;
            closeRequested = true;
            args.Cancel = true;
            scanStatus.Text = "正在结束蓝牙操作…";
        };
        Closed += (_, _) =>
        {
            toastTimer.Stop();
            overlay?.Close();
            Disconnect("已断开连接");
        };
        timer.Tick += (_, _) =>
        {
            if (bluetooth.IsDisconnected)
                Disconnect("设备已断开，请重新连接。");
            else if (clock.Elapsed.TotalSeconds - lastReading >= 15)
                Disconnect("心率数据已中断，请检查手环广播。");
            else
                RenderTrend();
        };
        toastTimer.Tick += (_, _) =>
        {
            toastTimer.Stop();
            toast.Visibility = Visibility.Collapsed;
        };
        RefreshButtons();
    }

    /// <summary>Opens the device picker and automatically starts discovery.</summary>
    /// <param name="sender">The dashboard button.</param>
    /// <param name="args">The WPF click event.</param>
    private async void OpenDevicesClick(object sender, RoutedEventArgs args)
    {
        dashboard.Visibility = Visibility.Collapsed;
        devicePage.Visibility = Visibility.Visible;
        await ScanAsync();
    }

    /// <summary>Returns from the device picker to the dashboard.</summary>
    /// <param name="sender">The back button.</param>
    /// <param name="args">The WPF click event.</param>
    private void BackClick(object sender, RoutedEventArgs args) => ShowDashboard();

    /// <summary>Refreshes the devices discovered by the picker.</summary>
    /// <param name="sender">The rescan button.</param>
    /// <param name="args">The WPF click event.</param>
    private async void RescanClick(object sender, RoutedEventArgs args) => await ScanAsync();

    /// <summary>Enables connection when the user selects a device.</summary>
    /// <param name="sender">The device list.</param>
    /// <param name="args">The WPF selection event.</param>
    private void DeviceSelectionChanged(object sender, SelectionChangedEventArgs args) => RefreshButtons();

    /// <summary>Connects to the device chosen in the picker.</summary>
    /// <param name="sender">The connect button.</param>
    /// <param name="args">The WPF click event.</param>
    private async void ConnectClick(object sender, RoutedEventArgs args)
    {
        if (devices.SelectedItem is BleHeartRateService.Candidate candidate)
            await ConnectAsync(candidate);
    }

    /// <summary>Ends the active connection and clears displayed readings.</summary>
    /// <param name="sender">The disconnect button.</param>
    /// <param name="args">The WPF click event.</param>
    private void DisconnectClick(object sender, RoutedEventArgs args) => Disconnect("已断开连接。");

    /// <summary>Reflows the trend when the chart changes size.</summary>
    /// <param name="sender">The chart canvas.</param>
    /// <param name="args">The WPF size event.</param>
    private void ChartSizeChanged(object sender, SizeChangedEventArgs args) => RenderTrend();

    /// <summary>Restores the main dashboard.</summary>
    private void ShowDashboard()
    {
        devicePage.Visibility = Visibility.Collapsed;
        dashboard.Visibility = Visibility.Visible;
        RenderTrend();
    }

    /// <summary>Reflects active Bluetooth operations and selection in the controls.</summary>
    private void RefreshButtons()
    {
        openDevices.IsEnabled = !busy && !connected;
        disconnect.IsEnabled = !busy && connected;
        back.IsEnabled = rescan.IsEnabled = devices.IsEnabled = !busy;
        connect.IsEnabled = !busy && devices.SelectedItem != null;
    }

    /// <summary>Searches nearby advertisements and fills the scrollable device list.</summary>
    /// <returns>A task that completes when the search result or error is displayed.</returns>
    private async Task ScanAsync()
    {
        busy = true;
        RefreshButtons();
        devices.ItemsSource = null;
        scanStatus.Text = "正在搜索附近蓝牙设备…";
        try
        {
            var results = await BleHeartRateService.ScanAsync();
            devices.ItemsSource = results;
            scanStatus.Text = results.Count > 0
                ? "请选择开启了心率广播的设备。"
                : "未发现设备，请开启电脑蓝牙和手环心率广播后重试。";
        }
        catch (Exception error) { scanStatus.Text = "搜索失败：" + error.Message; }
        finally { FinishOperation(); }
    }

    /// <summary>Subscribes to the selected device and returns to the dashboard on success.</summary>
    /// <param name="candidate">The advertisement selected by the user.</param>
    /// <returns>A task that completes after subscription or failure cleanup.</returns>
    private async Task ConnectAsync(BleHeartRateService.Candidate candidate)
    {
        busy = true;
        RefreshButtons();
        scanStatus.Text = "正在连接并订阅心率…";
        try
        {
            var version = ++connectionVersion;
            await Task.Run(() => bluetooth.SubscribeAsync(candidate, bpm => ShowHeartRate(bpm, version)));
            connected = true;
            connectionState.Text = "已连接 · " + (string.IsNullOrWhiteSpace(candidate.Name) ? candidate.ToString() : candidate.Name);
            dashboardMessage.Text = history.Count == 0 ? "等待设备发送心率…" : "正在接收心率";
            lastReading = clock.Elapsed.TotalSeconds;
            ShowDashboard();
            toastMessage.Text = "已连接 · " + (candidate.Name ?? "蓝牙设备");
            toast.Visibility = Visibility.Visible;
            toastTimer.Start();
            timer.Start();
        }
        catch (Exception error)
        {
            Disconnect("连接未完成。");
            scanStatus.Text = "连接失败：" + error.Message;
        }
        finally { FinishOperation(); }
    }

    /// <summary>Queues a BPM update and discards notifications from closed sessions.</summary>
    /// <param name="bpm">The decoded heart rate.</param>
    /// <param name="version">The session that produced the reading.</param>
    private void ShowHeartRate(int bpm, int version)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (connectionVersion != version) return;
            lastReading = clock.Elapsed.TotalSeconds;
            history.Enqueue((lastReading, bpm));
            reading.Text = bpm.ToString();
            dashboardMessage.Text = "正在接收心率";
            RenderTrend();
        });
    }

    /// <summary>Draws the most recent 60 seconds against elapsed time and labels the observed range.</summary>
    private void RenderTrend()
    {
        if (chart == null || chartHint == null || range == null) return;
        var now = clock.Elapsed.TotalSeconds;
        while (history.TryPeek(out var oldest) && now - oldest.Time > HeartRateTrend.HistorySeconds)
            history.Dequeue();
        chartHint.Visibility = history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        chartHint.Text = connected ? "等待心率数据" : "连接后显示心率趋势";
        range.Text = HeartRateTrend.Draw(chart, history, now);
        overlay?.UpdateReading(reading.Text, history, now);
    }

    /// <summary>Opens or activates the independent floating-window settings.</summary>
    /// <param name="sender">The settings button.</param>
    /// <param name="args">The WPF click event.</param>
    private void OpenOverlaySettingsClick(object sender, RoutedEventArgs args)
    {
        if (overlaySettings != null)
        {
            overlaySettings.Activate();
            return;
        }

        overlaySettings = new OverlaySettingsWindow(overlayOptions, ApplyOverlayOptions) { Owner = this };
        overlaySettings.Closed += (_, _) => overlaySettings = null;
        overlaySettings.Show();
    }

    /// <summary>Applies settings immediately and updates the floating window with the current reading.</summary>
    /// <param name="options">The latest settings snapshot.</param>
    private void ApplyOverlayOptions(OverlayOptions options)
    {
        overlayOptions = options;
        if (!options.Enabled)
        {
            overlay?.Hide();
            return;
        }

        if (overlay == null)
        {
            overlay = new HeartRateOverlay();
            overlay.Closed += (_, _) => overlay = null;
        }

        overlay.ApplyOptions(options.HeartRight, options.ShowTrend,
            options.ShowBackground, options.Locked);
        if (!overlay.IsVisible) overlay.Show();
        RenderTrend();
    }

    /// <summary>Releases Bluetooth resources and clears the dashboard for the next session.</summary>
    /// <param name="message">The reason displayed under the connection state.</param>
    private void Disconnect(string message)
    {
        timer.Stop();
        toastTimer.Stop();
        toast.Visibility = Visibility.Collapsed;
        connectionVersion++;
        connected = false;
        bluetooth.Dispose();
        history.Clear();
        reading.Text = "—";
        connectionState.Text = "未连接设备";
        dashboardMessage.Text = message;
        RenderTrend();
        RefreshButtons();
    }

    /// <summary>Restores buttons or closes the window after the current Bluetooth operation.</summary>
    private void FinishOperation()
    {
        busy = false;
        if (closeRequested) Close();
        else RefreshButtons();
    }
}
