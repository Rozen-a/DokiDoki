<div align="center">
  <h1>DokiDoki</h1>
  <p>通过蓝牙接收华为手环 9 实时心率的 Windows 桌面应用</p>
  <p>
    <img src="https://img.shields.io/badge/Windows-x64-0078D4" alt="Windows x64" />
    <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10" />
    <img src="https://img.shields.io/badge/UI-WPF-2563EB" alt="WPF" />
    <img src="https://img.shields.io/badge/Bluetooth-Low%20Energy-0063D1" alt="Bluetooth Low Energy" />
  </p>
</div>

## 快速开始

1. 克隆仓库，双击根目录的 [HeartRate.exe](./HeartRate.exe)。程序自带 .NET 和 WPF 运行时。
2. 戴好手环，在手环的「设置 → 心率广播」中开启广播。
3. 在应用中点击「扫描」，选中以 `HUAWEI Band HR` 开头的设备，再点击「连接」。
4. 收到有效读数后，窗口会显示当前 BPM。点击「断开」可停止接收并清空读数。

## 功能特性

- **蓝牙扫描与连接**。发现附近的 BLE 设备，并优先列出提供标准心率服务的设备。
- **实时心率显示**。接收华为手环 9 的心率通知，在窗口中显示当前 BPM。
- **连接状态提示**。断开连接或读数中断时，清空旧读数并提示当前状态。
- **本地处理**。心率数据仅在本地显示，不写入文件。

## 运行环境与验证状态

- **目标环境**为 Windows 10 22H2 或 Windows 11（x64），电脑需支持 BLE。
- **已验证** WPF 版在 Windows 10 22H2 上启动。旧版 Demo 已在同一系统上与华为手环 9 跑通扫描、连接、实时显示和断开。
- **待验证** WPF 版与手环的完整连接流程，以及 Windows 11 实机兼容性。

## 技术栈

| 用途 | 技术 |
| --- | --- |
| 桌面界面 | WPF、XAML |
| 应用 | C#、.NET 10 |
| 蓝牙 | Windows WinRT BLE GATT API，使用心率服务 `0x180D` 和测量特征 `0x2A37` |
| 发布 | Windows x64 自包含单文件 EXE |

## 开发与发布

### 在 VS Code 中运行

安装 Microsoft 的 C# 扩展，用 VS Code 打开仓库根目录。在左侧选择「运行和调试」，选中「运行 DokiDoki」并点击绿色三角，或按 F5。VS Code 会先构建再启动窗口。本机构建任务可使用此前下载的临时 SDK，其他电脑需安装 .NET 10 SDK。

### Visual Studio 或命令行

开发需要 Windows x64 和 .NET 10 SDK。使用支持 .NET 10 的 Visual Studio 并安装「.NET 桌面开发」工作负载后，打开 `DokiDoki.sln`，按 F5 启动调试。也可在仓库根目录打开 PowerShell，按需选择以下命令。

```powershell
dotnet build .\DokiDoki.sln
dotnet run --project .\DokiDoki.csproj
dotnet publish .\DokiDoki.csproj -c Release -o .\publish
```

发布文件为 `publish/HeartRate.exe`。如需清理本地构建产物，可删除 `bin/`、`obj/` 和 `publish/`。

## 项目结构

```text
DokiDoki/
├── .vscode/
│   ├── launch.json                # VS Code 一键启动及调试
│   └── tasks.json                 # 启动前编译 WPF 项目
├── Frontend/
│   ├── App.xaml                   # WPF 应用配置和启动窗口
│   ├── App.xaml.cs                # WPF 应用入口类
│   ├── HeartRateWindow.xaml       # 设备列表、按钮、状态和 BPM 布局
│   └── HeartRateWindow.xaml.cs    # 响应操作、更新窗口及处理读数超时
├── Backend/
│   ├── BleHeartRateService.cs     # BLE 扫描、GATT 连接、订阅及资源释放
│   └── BleMeasurement.cs          # WinRT 异步操作与心率测量数据解析
├── bin/                          # 构建时生成
│   ├── Debug/                    # VS Code 一键运行使用的调试输出
│   │   └── net10.0-windows10.0.19041.0/
│   │       └── win-x64/
│   │           └── HeartRate.exe # 调试入口
│   └── Release/                  # Release 构建输出
│       └── net10.0-windows10.0.19041.0/
│           └── win-x64/
│               └── HeartRate.exe # Release 构建的程序
├── DokiDoki.sln                   # Visual Studio 解决方案
├── DokiDoki.csproj                # .NET 10 WPF 项目及 x64 发布配置
├── global.json                    # .NET 10 SDK 版本选择
├── HeartRate.exe                  # 可直接运行的 Windows x64 程序
└── README.md                      # 使用、开发和发布说明
```

前端调用后端扫描并订阅心率，由 WPF 窗口负责显示结果。两层编译到同一个桌面程序，蓝牙服务在本机运行。目录树列出了 `bin/` 下的启动入口，同目录还包含构建生成的运行依赖。

### 前端源码

| 文件 | 代码职责 |
| --- | --- |
| `Frontend/App.xaml` | 声明 WPF 应用类，并用 `StartupUri` 指定启动窗口。 |
| `Frontend/App.xaml.cs` | 定义继承自 WPF `Application` 的 `App` 分部类，由 WPF 管理应用生命周期。 |
| `Frontend/HeartRateWindow.xaml` | 定义主窗口、蓝牙设备下拉框、扫描和连接按钮、BPM 数值及状态提示。 |
| `Frontend/HeartRateWindow.xaml.cs` | 绑定 UI 事件，调用蓝牙服务，并在 WPF 线程更新窗口。 |

`HeartRateWindow.xaml.cs` 中的构造函数绑定按钮、窗口关闭和定时器事件。定时器检查蓝牙断开和心率数据超时。

| 方法 | 职责 |
| --- | --- |
| `RefreshButtons()` | 根据忙碌状态、设备选择和订阅状态更新控件可用性及按钮文案。 |
| `ScanAsync()` | 调用后端扫描，填充设备列表，并优先选中符合条件的华为心率广播设备。 |
| `ConnectAsync()` | 在后台启动心率订阅，建立读数回调并启动超时检查。 |
| `ShowHeartRate()` | 将 BPM 送到 WPF 界面线程，过滤已结束连接留下的回调。 |
| `Disconnect()` | 停止定时器、释放蓝牙连接、清空读数并更新状态。 |
| `FinishOperation()` | 恢复界面操作，或在当前操作结束后响应此前的关闭请求。 |

### 后端源码

| 文件或类型 | 代码职责 |
| --- | --- |
| `Backend/BleHeartRateService.cs` | 管理单个 BLE 心率连接，持有设备、GATT 服务和测量特征。 |
| `BleHeartRateService.Candidate` | 保存设备名称、蓝牙地址、地址类型和心率服务标记。`ToString()` 生成列表显示文字。 |
| `Backend/BleMeasurement.cs` | 提供 WinRT 异步等待和 BLE 心率数据解析。 |

| 方法或属性 | 职责 |
| --- | --- |
| `BleHeartRateService.ScanAsync()` | 监听 BLE 广播，按地址去重，并将广播心率服务的设备排在前面。 |
| `BleHeartRateService.SubscribeAsync()` | 打开目标设备，查询心率服务 `0x180D` 和测量特征 `0x2A37`，启用通知。 |
| `BleHeartRateService.ReceiveHeartRate()` | 读取通知字节，解析后把有效 BPM 交给调用方。 |
| `BleHeartRateService.IsSubscribed` / `IsDisconnected` | 提供当前特征是否已建立及蓝牙设备是否断开的状态，供前端刷新界面。 |
| `BleHeartRateService.Dispose()` | 解除通知事件绑定，释放 GATT 服务和蓝牙设备。 |
| `BleMeasurement.WaitForAsync<T>()` | 将 Windows Runtime 异步操作转换为可等待的 .NET 任务。 |
| `BleMeasurement.TryDecode()` | 解析 8 位或 16 位 BPM，拒绝长度不足、无读数或传感器报告脱离皮肤的数据。 |

## 常见问题

- **扫描不到手环**。确认电脑蓝牙已开启、手环仍在心率广播界面。将手环靠近电脑后重扫。若仍找不到，可临时关闭手机蓝牙，再重新开启手环广播。
- **连不上或没有读数**。确认选中的是以 `HUAWEI Band HR` 开头的设备，手环贴合手腕且仍在广播。若应用提示读数中断，请重新扫描并连接。

## 文档参考

README 的展示结构参考了 [HeartRateMonitor-composeui](https://github.com/XiaochangXu/HeartRateMonitor-composeui)。
