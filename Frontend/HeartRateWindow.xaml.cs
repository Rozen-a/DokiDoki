// Coordinates the dashboard, device picker, and one live BLE session.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
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
    private ObsCaptureServer obsCapture;
    private OverlaySettingsWindow overlaySettings;
    private OverlayOptions overlayOptions = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer toastTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Queue<(double Time, int Bpm)> history = new();
    private readonly BleHeartRateService bluetooth = new();
    private double lastReading;
    private int connectionVersion;
    private CancellationTokenSource autoConnect;
    private ulong? rememberedAddress;
    private bool reconnecting;
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
            autoConnect?.Cancel();
            args.Cancel = true;
            scanStatus.Text = "正在结束蓝牙操作…";
        };
        Closing += (_, args) => { if (!args.Cancel) SaveOverlayPosition(); };
        Closed += (_, _) =>
        {
            toastTimer.Stop();
            obsCapture?.Dispose();
            obsCapture = null;
            overlay?.Close();
            Disconnect("已断开连接");
        };
        timer.Tick += (_, _) =>
        {
            if (bluetooth.IsDisconnected)
                _ = RecoverConnectionAsync("设备已断开");
            else if (clock.Elapsed.TotalSeconds - lastReading >= 15)
                _ = RecoverConnectionAsync("心率数据已中断");
            else
                RenderTrend();
        };
        toastTimer.Tick += (_, _) =>
        {
            toastTimer.Stop();
            toast.Visibility = Visibility.Collapsed;
            overlay?.ShowNotice(null);
        };
        Loaded += async (_, _) =>
        {
            var preferences = RecentDevice.LoadOptions();
            var saved = RecentDevice.LoadSettings(overlayOptions with
            {
                StartupConnect = preferences.Startup,
                Reconnect = preferences.Reconnect
            });
            try { ApplyOverlayOptions(saved); }
            catch (System.Net.HttpListenerException error)
            {
                ApplyOverlayOptions(saved with { Capture = false });
                RecentDevice.SaveSettings(saved);
                ShowToast("无法恢复直播采集：" + error.Message, warning: true);
            }
            rememberedAddress = RecentDevice.Load();
            if (overlayOptions.StartupConnect && rememberedAddress.HasValue) await AutoConnectAsync(rememberedAddress.Value, reconnect: false);
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
    private void DisconnectClick(object sender, RoutedEventArgs args)
    {
        if (autoConnect != null)
        {
            autoConnect.Cancel();
            disconnect.IsEnabled = false;
            return;
        }
        Disconnect("已断开连接。");
    }

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
        disconnect.IsEnabled = autoConnect != null || (!busy && connected);
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
        try { await SubscribeAsync(candidate, CancellationToken.None); }
        catch (Exception error)
        {
            Disconnect("连接未完成。");
            scanStatus.Text = "连接失败：" + error.Message;
        }
        finally { FinishOperation(); }
    }

    /// <summary>Subscribes to one device and remembers it after successful connection.</summary>
    /// <param name="candidate">The device to connect.</param>
    /// <param name="token">Cancels an automatic connection attempt.</param>
    /// <returns>A task completing when the connection is ready.</returns>
    private async Task SubscribeAsync(BleHeartRateService.Candidate candidate, CancellationToken token)
    {
        var version = ++connectionVersion;
        await Task.Run(() => bluetooth.SubscribeAsync(candidate, bpm => ShowHeartRate(bpm, version), token), token);
        token.ThrowIfCancellationRequested();
        rememberedAddress = candidate.Address;
        RecentDevice.Save(candidate.Address);
        connected = true;
        connectionState.Text = "已连接 · " + (string.IsNullOrWhiteSpace(candidate.Name) ? candidate.ToString() : candidate.Name);
        dashboardMessage.Text = history.Count == 0 ? "等待设备发送心率…" : "正在接收心率";
        lastReading = clock.Elapsed.TotalSeconds;
        ShowDashboard();
        ShowToast("已连接 · " + (candidate.Name ?? "蓝牙设备"));
        timer.Start();
    }

    /// <summary>Clears the lost connection, displays an alert, and starts retrying.</summary>
    /// <param name="reason">Reason that the current data stream stopped.</param>
    /// <returns>A task completing when automatic reconnection ends.</returns>
    private async Task RecoverConnectionAsync(string reason)
    {
        Disconnect(reason);
        var retry = overlayOptions.Reconnect && rememberedAddress.HasValue;
        ShowToast(retry ? reason + "，正在重连" : reason, warning: true, persistent: retry);
        if (retry) await AutoConnectAsync(rememberedAddress.Value, reconnect: true);
    }

    /// <summary>Polls until startup connects, or retries an interrupted session for two minutes.</summary>
    /// <param name="address">The only BLE address eligible for automatic connection.</param>
    /// <param name="reconnect">True for recovery of a lost session.</param>
    /// <returns>A task completing on connection, cancellation or timeout.</returns>
    private async Task AutoConnectAsync(ulong address, bool reconnect)
    {
        using var stop = new CancellationTokenSource();
        using var deadline = new CancellationTokenSource();
        if (reconnect) deadline.CancelAfter(TimeSpan.FromMinutes(2));
        reconnecting = reconnect;
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(stop.Token, deadline.Token);
        autoConnect = stop;
        busy = true;
        connectionState.Text = reconnect ? "正在重连" : "自动连接中";
        dashboardMessage.Text = "正在搜索上次连接的设备。";
        RefreshButtons();
        try
        {
            while (true)
            {
                attempt.Token.ThrowIfCancellationRequested();
                try
                {
                    var results = await BleHeartRateService.ScanAsync(attempt.Token, scanMilliseconds: 3000);
                    var candidate = results.FirstOrDefault(item => item.Address == address);
                    if (candidate != null)
                    {
                        await SubscribeAsync(candidate, attempt.Token);
                        return;
                    }
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    ClearFailedAttempt();
                }
                await Task.Delay(250, attempt.Token);
            }
        }
        catch (OperationCanceledException)
        {
            ClearFailedAttempt();
            connectionState.Text = "已关闭自动连接";
            dashboardMessage.Text = "可点击「连接设备」手动连接。";
            if (!stop.IsCancellationRequested)
                ShowToast("自动连接超时，已停止重连", warning: true);
            else
            {
                toast.Visibility = Visibility.Collapsed;
                overlay?.ShowNotice(null);
            }
        }
        finally
        {
            autoConnect = null;
            reconnecting = false;
            FinishOperation();
        }
    }

    /// <summary>Releases a partial subscription without interrupting the retry indicator or alerts.</summary>
    private void ClearFailedAttempt()
    {
        connectionVersion++;
        bluetooth.Dispose();
        history.Clear();
        reading.Text = "—";
        dashboardHeart.SetRate(0);
        RenderTrend();
    }

    /// <summary>Shows a non-modal status message and mirrors warnings to the desktop overlay.</summary>
    /// <param name="message">Text shown in the alert.</param>
    /// <param name="warning">Whether to highlight and mirror this message.</param>
    /// <param name="persistent">Keeps the notice visible until replaced or cleared.</param>
    private void ShowToast(string message, bool warning = false, bool persistent = false)
    {
        toastTimer.Stop();
        toastTimer.Interval = TimeSpan.FromSeconds(warning ? 7 : 3);
        toast.Background = warning ? System.Windows.Media.Brushes.MistyRose : System.Windows.Media.Brushes.LavenderBlush;
        toastMessage.Foreground = warning ? System.Windows.Media.Brushes.DarkRed : System.Windows.Media.Brushes.Black;
        toastMessage.Text = message;
        toast.Visibility = Visibility.Visible;
        overlay?.ShowNotice(warning ? message : null);
        if (!persistent) toastTimer.Start();
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
            dashboardHeart.SetRate(bpm);
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
        range.Text = HeartRateTrend.Draw(chart, history, now, showAxis: true);
        overlay?.UpdateReading(reading.Text, history, now);
        obsCapture?.Update(JsonSerializer.Serialize(new
        {
            bpm = reading.Text,
            pulseTempo = PulsingHeart.GetTempo(history.TryPeek(out _) ? history.Last().Bpm : 0),
            heartRight = overlayOptions.HeartRight,
            showTrend = overlayOptions.ShowTrend,
            showBackground = overlayOptions.ShowBackground,
            hideBpm = overlayOptions.HideBpm,
            points = history.Select(sample => new
            {
                bpm = sample.Bpm,
                position = 1 - (now - sample.Time) / HeartRateTrend.HistorySeconds
            })
        }));
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

        overlaySettings = new OverlaySettingsWindow(overlayOptions, options => ApplyOverlayOptions(options with { Enabled = overlayOptions.Enabled, Capture = overlayOptions.Capture })) { Owner = this };
        overlaySettings.Closed += (_, _) => overlaySettings = null;
        overlaySettings.Show();
    }

    /// <summary>Toggles the desktop overlay independently of capture.</summary>
    /// <param name="sender">The desktop toggle.</param>
    /// <param name="args">The WPF click event.</param>
    private void ToggleDesktopClick(object sender, RoutedEventArgs args)
    {
        ApplyOverlayOptions(overlayOptions with { Enabled = !overlayOptions.Enabled });
    }

    /// <summary>Toggles browser capture and reports listener errors without blocking the UI.</summary>
    /// <param name="sender">The capture toggle.</param>
    /// <param name="args">The WPF click event.</param>
    private void ToggleCaptureClick(object sender, RoutedEventArgs args)
    {
        try { ApplyOverlayOptions(overlayOptions with { Capture = !overlayOptions.Capture }); }
        catch (System.Net.HttpListenerException error)
        {
            if (!reconnecting) ShowToast("无法开启直播采集：" + error.Message, warning: true);
        }
    }

    /// <summary>Applies settings immediately and updates the floating window with the current reading.</summary>
    /// <param name="options">The latest settings snapshot.</param>
    private void ApplyOverlayOptions(OverlayOptions options)
    {
        if (options.Capture)
            obsCapture ??= new ObsCaptureServer(ReadCaptureAsset("ObsOverlay.html"), ReadCaptureAsset("Heart.png"));
        else
        {
            obsCapture?.Dispose();
            obsCapture = null;
        }

        if (overlay != null && double.IsFinite(overlay.Left) && double.IsFinite(overlay.Top))
            options = options with { OverlayLeft = overlay.Left, OverlayTop = overlay.Top };
        if (overlayOptions != options) RecentDevice.SaveSettings(options);
        overlayOptions = options;
        desktopToggle.Content = options.Enabled ? "桌面悬浮窗 · 开" : "桌面悬浮窗";
        captureToggle.Content = options.Capture ? "直播采集 · 开" : "直播采集";
        desktopToggle.Background = options.Enabled ? (System.Windows.Media.Brush)FindResource("OptionActiveBrush") : (System.Windows.Media.Brush)FindResource("OptionIdleBrush");
        captureToggle.Background = options.Capture ? (System.Windows.Media.Brush)FindResource("OptionActiveBrush") : (System.Windows.Media.Brush)FindResource("OptionIdleBrush");
        if (autoConnect != null && !(reconnecting ? options.Reconnect : options.StartupConnect)) autoConnect.Cancel();
        if (!options.Enabled)
        {
            overlay?.Hide();
            RenderTrend();
            return;
        }

        if (overlay == null)
        {
            // Keep the overlay independent of minimization. Closed handles its lifetime.
            overlay = new HeartRateOverlay();
            if (options.OverlayLeft is double left && options.OverlayTop is double top &&
                double.IsFinite(left) && double.IsFinite(top) &&
                left >= SystemParameters.VirtualScreenLeft &&
                left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 30 &&
                top >= SystemParameters.VirtualScreenTop &&
                top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 30)
            {
                overlay.WindowStartupLocation = WindowStartupLocation.Manual;
                overlay.Left = left;
                overlay.Top = top;
            }
            overlay.Closed += (_, _) => overlay = null;
        }

        overlay.ApplyOptions(options.HeartRight, options.ShowTrend,
            options.ShowBackground, options.Locked, options.HideBpm);
        if (!overlay.IsVisible) overlay.Show();
        if (toast.Visibility == Visibility.Visible && toastMessage.Foreground == System.Windows.Media.Brushes.DarkRed) overlay.ShowNotice(toastMessage.Text);
        RenderTrend();
    }

    /// <summary>Persists the current floating window position before orderly shutdown.</summary>
    private void SaveOverlayPosition()
    {
        if (overlay == null || !double.IsFinite(overlay.Left) || !double.IsFinite(overlay.Top)) return;
        overlayOptions = overlayOptions with { OverlayLeft = overlay.Left, OverlayTop = overlay.Top };
        RecentDevice.SaveSettings(overlayOptions);
    }

    /// <summary>Reads a static resource for the local OBS browser page.</summary>
    /// <param name="name">File name under Frontend/Assets.</param>
    /// <returns>The asset bytes.</returns>
    private static byte[] ReadCaptureAsset(string name)
    {
        using var stream = Application.GetResourceStream(new Uri("/Frontend/Assets/" + name, UriKind.Relative)).Stream;
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
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
        dashboardHeart.SetRate(0);
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
