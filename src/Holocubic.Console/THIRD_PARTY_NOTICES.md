# 第三方组件说明

本项目使用或兼容以下第三方组件。组件版权和许可仍归各自作者所有：

- [Microsoft.Web.WebView2](https://www.nuget.org/packages/Microsoft.Web.WebView2)
- [LibreHardwareMonitorLib](https://www.nuget.org/packages/LibreHardwareMonitorLib)
- [PawnIO](https://github.com/namazso/PawnIO.Setup)：PCAPP 仅检测安装状态并链接至官方安装程序，不捆绑驱动
- [HWiNFO](https://www.hwinfo.com/)：仅提供默认关闭的 Shared Memory 兼容接口，不捆绑程序，用户须遵守其许可条款
- [System.Management](https://www.nuget.org/packages/System.Management)：用于读取 Windows WMI 静态硬件信息
- [SixLabors.ImageSharp](https://www.nuget.org/packages/SixLabors.ImageSharp)
- [Node.js](https://nodejs.org/)：许可证文本见 `src/Clocteck.CubicCenter/CompanionServices/node/LICENSE`
- [holocubic-smtc-music](https://github.com/clocteck/holocubic-smtc-music)：SMTC Music 设备端应用与 JavaScript 兼容 Bridge 来源
- [ColdMistyRain/smtc-brige](https://github.com/ColdMistyRain/smtc-brige)：内置 Rust SMTC Bridge 的上游项目，PCAPP 版增加了 QQ 音乐进度校正与封面兼容修复

NuGet 依赖会在还原和构建时下载，本仓库不提交其二进制构建产物。
