# DownloadPhysicsSDKs.ps1
# 下载并配置物理引擎 SDK (Box2D 和 Jolt Physics)
# 使用方式: .\DownloadPhysicsSDKs.ps1

param(
    [string]$Box2DVersion = "v2.4.1",
    [string]$JoltVersion = "v5.1.0",
    [string]$OutputDir = "engine\sdk"
)

$ErrorActionPreference = "Stop"

# 获取脚本所在目录
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  物理引擎 SDK 下载脚本" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 创建输出目录
$Box2DDir = Join-Path $RootDir "$OutputDir\box2d"
$JoltDir = Join-Path $RootDir "$OutputDir\jolt"

if (-not (Test-Path $Box2DDir)) {
    New-Item -ItemType Directory -Path $Box2DDir -Force | Out-Null
}
if (-not (Test-Path $JoltDir)) {
    New-Item -ItemType Directory -Path $JoltDir -Force | Out-Null
}

# ============================================
# 下载 Box2D
# ============================================
Write-Host "[1/2] 下载 Box2D $Box2DVersion ..." -ForegroundColor Yellow

$Box2DZipUrl = "https://github.com/erincatto/box2d/releases/download/$Box2DVersion/box2d-$Box2DVersion.zip"
$Box2DZipPath = Join-Path $RootDir "box2d-$Box2DVersion.zip"

try {
    Write-Host "  从 GitHub 下载..."
    Invoke-WebRequest -Uri $Box2DZipUrl -OutFile $Box2DZipPath -UseBasicParsing

    Write-Host "  解压 Box2D..."
    Expand-Archive -Path $Box2DZipPath -DestinationPath $Box2DDir -Force

    # 移动文件到正确位置
    $ExtractedDir = Join-Path $Box2DDir "box2d-$Box2DVersion"
    if (Test-Path $ExtractedDir) {
        Get-ChildItem -Path $ExtractedDir -File | Move-Item -Destination $Box2DDir -Force
        Get-ChildItem -Path $ExtractedDir -Directory | Move-Item -Destination $Box2DDir -Force
        Remove-Item $ExtractedDir -Recurse -Force
    }

    Remove-Item $Box2DZipPath -Force
    Write-Host "  Box2D 下载完成!" -ForegroundColor Green
}
catch {
    Write-Host "  Box2D 下载失败: $_" -ForegroundColor Red
    Write-Host "  请手动下载: $Box2DZipUrl" -ForegroundColor Yellow
}

Write-Host ""

# ============================================
# 下载 Jolt Physics
# ============================================
Write-Host "[2/2] 下载 Jolt Physics $JoltVersion ..." -ForegroundColor Yellow

$JoltZipUrl = "https://github.com/jrouwe/JoltPhysics/releases/download/$JoltVersion/Jolt-$JoltVersion.zip"
$JoltZipPath = Join-Path $RootDir "Jolt-$JoltVersion.zip"

try {
    Write-Host "  从 GitHub 下载..."
    Invoke-WebRequest -Uri $JoltZipUrl -OutFile $JoltZipPath -UseBasicParsing

    Write-Host "  解压 Jolt Physics..."
    Expand-Archive -Path $JoltZipPath -DestinationPath $JoltDir -Force

    # 移动文件到正确位置
    $ExtractedDir = Join-Path $JoltDir "Jolt-$JoltVersion"
    if (Test-Path $ExtractedDir) {
        Get-ChildItem -Path $ExtractedDir -File | Move-Item -Destination $JoltDir -Force
        Get-ChildItem -Path $ExtractedDir -Directory | Move-Item -Destination $JoltDir -Force
        Remove-Item $ExtractedDir -Recurse -Force
    }

    Remove-Item $JoltZipPath -Force
    Write-Host "  Jolt Physics 下载完成!" -ForegroundColor Green
}
catch {
    Write-Host "  Jolt Physics 下载失败: $_" -ForegroundColor Red
    Write-Host "  请手动下载: $JoltZipUrl" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  下载完成!" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "目录结构:" -ForegroundColor White
Write-Host "  Box2D:  $Box2DDir"
Write-Host "  Jolt:   $JoltDir"
Write-Host ""
Write-Host "如果手动下载，请确保 SDK 头文件在 include 目录，库文件在 lib 目录" -ForegroundColor Gray
