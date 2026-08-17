# 一键构建 DeepSeek Harness Desktop（app + setup）
# 需要：Windows + 系统自带 csc（.NET Framework 4.x）+ Node.js（打包 node 运行时）
$ErrorActionPreference = "Stop"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { throw "未找到 csc.exe（需要 .NET Framework 4.x）" }

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root

# 0. node 运行时（若没有则从 PATH 拷贝）
if (-not (Test-Path "node\node.exe")) {
    $node = (Get-Command node).Source
    if (-not $node) { throw "未找到 node，请先安装 Node.js" }
    New-Item -ItemType Directory -Force -Path "node" | Out-Null
    Copy-Item $node "node\node.exe"
}

# 1. 编译窗口壳
Write-Host "== 编译 DeepSeek.Harness.Desktop.exe =="
& $csc /nologo /target:winexe /optimize+ /platform:x64 /win32manifest:app.manifest /win32icon:harness.ico `
  /out:DeepSeek.Harness.Desktop.exe `
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
  /r:Microsoft.Web.WebView2.Core.dll /r:Microsoft.Web.WebView2.WinForms.dll `
  src\MainForm.cs
if ($LASTEXITCODE -ne 0) { throw "窗口壳编译失败" }

# 2. 压缩 node 运行时（供安装器嵌入）
Write-Host "== 压缩 node 运行时 =="
$in = [System.IO.File]::OpenRead("node\node.exe")
$fs = [System.IO.File]::Create("_node.pak")
$ds = New-Object System.IO.Compression.DeflateStream($fs, [System.IO.Compression.CompressionLevel]::Optimal)
$in.CopyTo($ds); $ds.Close(); $fs.Close(); $in.Close()
Write-Host ("node.pak: {0:N1} MB" -f ((Get-Item _node.pak).Length / 1MB))

# 3. 编译安装器（嵌入全部文件）
Write-Host "== 编译 DeepSeek.Harness.Desktop-Setup.exe =="
& $csc /nologo /target:winexe /optimize+ /platform:x64 /win32manifest:app.manifest /win32icon:harness.ico `
  /out:DeepSeek.Harness.Desktop-Setup.exe `
  "/resource:DeepSeek.Harness.Desktop.exe,payload.app.exe" `
  "/resource:_node.pak,payload.node.pak" `
  "/resource:Microsoft.Web.WebView2.Core.dll,payload.core.dll" `
  "/resource:Microsoft.Web.WebView2.WinForms.dll,payload.winforms.dll" `
  "/resource:WebView2Loader.dll,payload.loader.dll" `
  "/resource:store.html,payload.store.html" `
  "/resource:harness.ico,payload.harness.ico" `
  "/resource:app.manifest,payload.manifest" `
  "/resource:src\MainForm.cs,payload.MainForm.cs" `
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
  src\SetupWizard.cs
if ($LASTEXITCODE -ne 0) { throw "安装器编译失败" }

Remove-Item _node.pak -Force -ErrorAction SilentlyContinue
Pop-Location
Write-Host "== 构建完成 =="
Get-Item "$root\DeepSeek.Harness.Desktop.exe", "$root\DeepSeek.Harness.Desktop-Setup.exe" | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}}
