# DeepSeek Harness Desktop

自己打包的 DeepSeek Harness 桌面客户端：一个 WebView2 窗口壳，内置 Node 运行时和扩展商店，双击即用，不依赖任何第三方桌面板。

- 独立窗口聊天，界面与 DSH 网页版完全一致
- 数据与本机网页版共用（`~/.dsh`），会话、模型、插件配置全都在
- 内置 Node 运行时，无需安装 Node.js
- 扩展商店：浏览 npm 上全部 dsh 插件（1142+），一键安装
- 标准安装向导：选路径 → 安装 → 勾选启动 + 桌面快捷方式
- 高 DPI 适配，动画不被系统"减少动态效果"静音

## 下载

到 Releases 页面下载 `DeepSeek.Harness.Desktop-Setup.exe`（含安装向导）或 `DeepSeek.Harness.Desktop.exe`（绿色版，需与同目录 dll 一起）。

## 使用

双击安装包 → 安装向导 → 完成页勾选"启动"和"创建桌面快捷方式"。

窗口顶部工具栏：
- **聊天**：回到聊天界面
- **扩展商店**：打开扩展商店（顶部热门推荐 + 全部扩展，可搜索、一键安装）
- **刷新**：刷新页面

装完扩展后重启应用生效。数据目录是 `~/.dsh`，和网页版同一个。

## 自己构建

需要：Windows、.NET Framework 4.x（系统自带 csc）、Node.js（仅构建安装包时打包 node 运行时用）。

```powershell
# 1. 编译窗口壳
.\build.ps1
```

`build.ps1` 会：
1. 用系统 csc 编译 `DeepSeek.Harness.Desktop.exe`（WebView2 窗口壳 + 商店服务）
2. 压缩 node 运行时
3. 编译 `DeepSeek.Harness.Desktop-Setup.exe`（安装向导，全部文件嵌入）

## 结构

```
src/MainForm.cs      窗口壳：WebView2 加载 DSH Web UI + 启动服务 + 工具栏
src/SetupWizard.cs   安装器：向导（欢迎/路径/进度/完成）+ 嵌入资源解压 + 桌面快捷方式
store.html           扩展商店页面（npm dsh-plugin 列表，可搜索/一键安装）
app.manifest         高 DPI 声明
harness.ico          图标
```

## 技术说明

- 窗口壳用 WinForms + WebView2（系统自带运行时），不引入 Electron
- 本地商店服务用 `TcpListener` 手写极简 HTTP（免 urlacl），托管 `store.html` 并提供 `/install`（`dsh plugin --profile web add <pkg>`）、`/installed` 接口
- 启动时探测 `127.0.0.1:3080`，已有网页版就直接连，没有才自己起服务
- 浏览器参数：禁用后台节流、允许 WebGL 软件渲染、`--force-prefers-no-reduced-motion`（保证流体/粒子动画不被系统"减少动态效果"静音）

## License

MIT
