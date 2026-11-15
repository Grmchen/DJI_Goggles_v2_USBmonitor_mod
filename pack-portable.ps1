param(
    [string]$SunshinePath = "C:\Program Files\Sunshine",
    [string]$Out = ".\GogglesUsb-portable"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
if (-not (Test-Path (Join-Path $SunshinePath "sunshine.exe"))) {
    throw "没找到 Sunshine：$SunshinePath（先装好 Sunshine 或换 -SunshinePath）"
}
Write-Host "1/3 编译…"
dotnet build (Join-Path $root "src\GogglesUsb\GogglesUsb.csproj") -c Release | Out-Null
$bin = Join-Path $root "src\GogglesUsb\bin\Release\net10.0-windows"
$dst = Join-Path $root $Out
if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
New-Item -ItemType Directory -Force -Path $dst | Out-Null
Write-Host "2/3 复制程序与 Sunshine…"
Get-ChildItem $bin -File | Where-Object { $_.Extension -in '.exe', '.dll', '.json' } |
    Copy-Item -Destination $dst -Force
$sun = Join-Path $dst "sunshine"
New-Item -ItemType Directory -Force -Path $sun | Out-Null
Get-ChildItem $SunshinePath -Force | Where-Object { $_.Name -ne 'config' } |
    ForEach-Object { Copy-Item $_.FullName -Destination $sun -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $sun "config") | Out-Null
Copy-Item (Join-Path $root "README.md") -Destination $dst -Force
Write-Host "3/3 完成"
$sum = (Get-ChildItem $dst -Recurse -File | Measure-Object Length -Sum)
Write-Host ("便携目录：{0}（{1:N1} MB）" -f $dst, ($sum.Sum / 1MB)) -ForegroundColor Green
Write-Host "把整个文件夹拷走即可；转发器和配对密钥会在第一次运行时自动下载/生成到该文件夹里。"
