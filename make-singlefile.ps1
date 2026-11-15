
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$proj = Join-Path $root "src\GogglesUsb\GogglesUsb.csproj"
$out = Join-Path $root "dist-single"

Write-Host "正在发布单文件自包含版（首次会下载 runtime pack）…"
dotnet publish $proj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -o $out
if ($LASTEXITCODE -ne 0) { throw "发布失败" }

Copy-Item (Join-Path $root "README.md") -Destination $out -Force
$exe = Join-Path $out "GogglesUsb.exe"
$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host ("完成：{0}（{1} MB，单文件，免安装运行时）" -f $exe, $size) -ForegroundColor Green
Write-Host "第一次运行会把这个 exe 旁边的 sunshine\ 和 relay\ 自动下载好（约 70MB），之后就完全离线可用。"