# Holocubic 控制台（Windows）

当前版本：**0.4.7**。基于 Electron 的 Holocubic 桌面控制台。

## 下载与使用

从 [Releases](https://github.com/ClocTeck-Jie/holocubic-pc-station/releases/latest) 下载 `holocubic-console-v0.4.7-win-x64.zip`，完整解压后打开文件夹中的 **holocubic控制台.exe**。请保留旁边的 DLL、locales 和 resources 文件夹。

## 功能

- 扫描局域网并自动添加设备、手动添加、设备重命名。
- 在内嵌浏览器中打开设备 Web 应用，应用控制窗口独立弹出。
- 文件管理、上传下载、传输队列、进度与速度显示。
- 串口配网、串口日志、设备重启与异常记录。
- 电脑性能监控、传感器数据映射、音乐、Holopet、AI 状态和桌面镜像服务。
- 简体中文、繁体中文、英语、日语、德语界面。
- 软件更新清单与 SHA-256 校验。AI 状态、额度和上下文信息取决于对应客户端可提供的数据，并非所有来源均提供全部字段。

## 更新

完整程序包适合首次安装。`holocubic-update-0.4.7.zip` 是控制台应用更新包，不包含 Electron 运行时。
可在软件更新设置中使用清单地址：
`https://github.com/ClocTeck-Jie/holocubic-pc-station/releases/latest/download/latest.json`

## 源码和构建

当前实现位于 `src/Holocubic.Console`；原 WPF 实现保留在 `src/Clocteck.CubicCenter`，旧说明见 [历史说明](docs/legacy-pc-station.md)。

Windows 构建需要 Electron 43.6.0（win32-x64）、.NET 10 SDK 和 Rust MSVC 工具链：

1. 下载并解压官方 Electron 43.6.0 Windows x64 运行时。
2. 将 `src/Holocubic.Console` 复制到运行时的 `resources/app`。
3. 执行 `dotnet publish src/Holocubic.Console/native-source/HoloNative.csproj -c Release -r win-x64 --self-contained true -o native-build`，将 `HoloNative.exe` 放入 `resources/app/native`。
4. 在 `src/SmtcBridgeRust` 执行 `cargo build --release --locked`，将生成的桥接程序复制为 `resources/app/services/smtc/smtc-bridge.exe`。
5. 将运行时 `electron.exe` 重命名为 `holocubic控制台.exe` 并启动。应用图标可在打包时使用 `resources/app/assets/app.ico`。

仓库不提交运行时和编译后二进制；这些包含在 Release 完整包中。

## 已知问题

360 曾对内置 `smtc-bridge.exe` 报告 `HEUR/QVM202.0.F926.Malware.Gen`。目前尚未获得 360 的核验结果，本版仍包含该组件，并未宣称已解决该检测。不要通过关闭安全防护绕过提示。

## 许可

控制台采用 GPL-3.0-only；第三方组件保留各自许可，见 LICENSE 和 THIRD_PARTY_NOTICES.md。Electron 的许可随完整程序包附带。
