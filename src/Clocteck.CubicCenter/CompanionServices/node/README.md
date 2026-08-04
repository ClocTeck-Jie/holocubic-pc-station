# Node.js runtime

SMTC Music 桥接服务需要 Node.js。源码仓库不包含体积较大的 `node.exe`。

- 开发环境：安装 Node.js 并确保 `node.exe` 位于系统 `PATH`。
- 便携发布：将 Windows x64 Node.js 可执行文件复制为本目录的 `node.exe`。

程序会优先使用本目录的便携运行时，不存在时回退到系统 Node.js。
