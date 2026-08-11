# Holocubic PC Station

Holocubic PC Station 是面向 Clocteck Cubic / Holocubic 设备的 Windows 电脑端控制中心。当前程序内部名称为 **Clocteck Cubic Center**，使用 C#、.NET 10、WPF 和内置 WebView2 界面实现。

## 主要功能

- 发现局域网设备，也可以手动输入 IPv4 地址连接多台设备。
- 查看设备状态、当前应用、Wi-Fi RSSI 和电脑端服务状态。
- 启动设备应用，并在软件内分屏加载应用控制页面。
- 浏览应用商店、查看介绍、下载应用包，并通过固件 FS 或 DevTools 接口安装到设备。
- 管理语言、天气、时区、亮度、息屏、闹钟、服务和固件更新。
- 通过固件 FS API 浏览、上传、下载、重命名、复制和删除设备文件。
- 上传图片或 GIF 时按设备屏幕比例处理媒体；Lua 文件可跳转到开发工具编辑。
- 串口实时输出、软件运行日志和 Lua 开发工具。
- 通过 USB 串口为设备扫描 WiFi 并发送配网信息，无需让电脑切换到设备热点。
- 对多台设备执行 FS、DevTools 或 RAM 网络吞吐、碎片文件和 API 延迟测试。
- 配置并启动 320 × 240 桌面投屏服务，支持显示器、虚拟副屏和指定区域。
- 内置 Holo PC Monitor、Holopet、Codex Buddy 和 SMTC Music 等电脑端兼容服务。

## 0.1.0 更新内容

- 首个公开发布版本，提供设备发现、应用管理、应用商店、设备设置和服务管理。
- 新增固件 FS 文件管理、Lua 编辑、串口日志、网络测速和固件更新界面。
- 集成 Holo PC Monitor、Holopet、Codex Buddy、SMTC Music 与桌面投屏电脑服务。
- 新增 WiFi Setting Guide USB 串口配网流程，扫描时自动连接串口并显示准确的密码错误信息。
- 支持按设备 IP 管理多台设备，并根据设备当前应用自动启动或停止对应电脑服务。

## 环境要求

- Windows 10/11 x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Microsoft Edge WebView2 Runtime
- 可选：Node.js 24 或兼容版本，仅 SMTC Music 服务需要
- 从源码运行桌面投屏时需要 Python 3.12、Pillow 和 mss；Release ZIP 已内置所需 Python 环境

应用管理、文件管理和设备设置不依赖 Node.js。

桌面投屏源码依赖可通过以下命令安装：

```powershell
py -3 -m pip install -r .\src\Clocteck.CubicCenter\CompanionServices\desktop-mirror\requirements.txt
```

## 编译运行

```powershell
dotnet restore .\src\Clocteck.CubicCenter\Clocteck.CubicCenter.csproj
dotnet run --project .\src\Clocteck.CubicCenter\Clocteck.CubicCenter.csproj
```

生成 Release 版本：

```powershell
dotnet publish .\src\Clocteck.CubicCenter\Clocteck.CubicCenter.csproj `
  -c Release -r win-x64 --self-contained true `
  -o .\artifacts\win-x64
```

## SMTC Music 与 Node.js

源码仓库不提交约 92 MB 的第三方 `node.exe`。程序启动 SMTC Music 服务时按以下顺序查找：

1. `CompanionServices/node/node.exe`
2. 系统 `PATH` 中的 `node.exe`

需要制作完全便携的发布包时，可以将 Windows x64 版 Node.js 可执行文件放到：

```text
src/Clocteck.CubicCenter/CompanionServices/node/node.exe
```

Node.js 的许可证文本保留在同一目录的 `LICENSE` 中。

## 运行数据

程序采用便携模式，将本机设置、设备列表、服务配置、应用下载缓存和 WebView2 数据保存在运行目录中。这些内容可能包含局域网设备地址和个人设置，已通过 `.gitignore` 排除，不应提交到仓库。

## 相关项目

- [holocubic-apps](https://github.com/clocteck/holocubic-apps)
- [holopet](https://github.com/clocteck/holopet)
- [codex_buddy](https://github.com/clocteck/codex_buddy)
- [desktop-mirror](https://github.com/clocteck/desktop-mirror)
- [holocubic-smtc-music](https://github.com/clocteck/holocubic-smtc-music)

## 许可证

项目以 GNU General Public License v3.0 发布。第三方组件仍遵循各自许可证，详见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
