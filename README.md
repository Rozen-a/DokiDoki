# DokiDoki

一个轻量的 Windows 桌面应用，通过蓝牙低功耗（BLE）接收华为手环 9 的实时心率，并以 BPM 显示。

## 运行环境

- Windows 10 或 Windows 11，电脑需支持 BLE。当前构建脚本使用 64 位 Windows 自带的编译器。
- 使用 Windows 自带的 .NET Framework 4.8 或 4.8.1。
- 已在 Windows 10 22H2 和华为手环 9 上验证扫描、连接、显示心率和断开。Windows 11 待实机验收。

## 克隆后直接运行

仓库根目录随源码提供预编译的 `HeartRate.exe`。克隆后即可双击运行。

1. 戴好手环，在手环「设置 → 心率广播」中开启广播。
2. 双击 `HeartRate.exe` 并点击「扫描」。
3. 选择以 `HUAWEI Band HR` 开头的设备，点击「连接」。收到第一条有效读数后，窗口会显示 BPM。
4. 点击「断开」可结束接收并清空读数。

已验证的手环可直接连接，无需在 Windows 设置中预先配对。如果扫不到手环，先确认电脑蓝牙已开启、手环仍在广播，再将手环靠近电脑并重扫。若仍找不到，可临时关闭手机蓝牙，重新开启手环广播后再试。

## 从源码重新构建

在仓库根目录打开 PowerShell，运行：

```powershell
.\Build.ps1
```

脚本使用 Windows 自带的 .NET Framework 编译器，在仓库根目录生成 `HeartRate.exe`。

## 项目结构

- `HeartRateWindow.cs` 实现 WinForms 窗口与 BLE 连接流程。
- `Build.ps1` 编译桌面应用。
- `probe/HeartRateProbe.cs` 保留命令行探针源码，桌面应用复用其中的 WinRT 异步等待和心率解析方法。

应用通过 Windows 原生 BLE GATT API 查询标准心率服务 `0x180D`，订阅心率测量特征 `0x2A37`。心率在本地实时显示，不写入文件。
