
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$proj = Join-Path $root "src\GogglesUsb\GogglesUsb.csproj"
$out = Join-Path $root "src\GogglesUsb\bin\Release\net10.0-windows"
$dist = Join-Path $root "dist"

dotnet build $proj -c Release
if ($LASTEXITCODE -ne 0) { throw "编译失败" }

New-Item -ItemType Directory -Force -Path $dist | Out-Null
Get-ChildItem $out -File |
    Where-Object { $_.Extension -in '.exe', '.dll', '.json' } |
    Copy-Item -Destination $dist -Force

Write-Host ""
Write-Host "完成：$dist" -ForegroundColor Green
Get-ChildItem $dist | Select-Object Name, Length | Format-Table -AutoSize
Write-Host "提示：直接把 dist 整个打包发出去即可（需要 .NET 10 Desktop Runtime）。"