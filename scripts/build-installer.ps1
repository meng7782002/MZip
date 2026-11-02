<#
.SYNOPSIS
    构建 JsjmZip Windows 安装包。
.DESCRIPTION
    1. 读取项目版本号（可由 -Version 覆盖）
    2. dotnet publish 自包含发布到 build\publish
    3. 定位 ISCC.exe 并编译 installer\JsjmZip.iss
    4. 输出 dist\JsjmZip-<版本>-Setup.exe
.PARAMETER Version
    安装包版本号，默认读取 JsjmZip.csproj 中的 Version。
.PARAMETER SkipPublish
    跳过发布步骤，直接使用已有的 build\publish 内容编译安装包。
#>
param(
    [string]$Version,
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$logFile = 'F:\AITEMP\JsjmZip-installer-build.log'

function Write-BuildLog([string]$message) {
    $line = "[{0}] {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $message
    Write-Host $line
    try {
        $dir = Split-Path -Parent $logFile
        if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
        Add-Content -Path $logFile -Value $line -Encoding UTF8
    } catch {
        Write-Warning ("写入构建日志失败：" + $_.Exception.Message)
    }
}

try {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $csproj = Join-Path $repoRoot 'src\JsjmZip\JsjmZip.csproj'
    $publishDir = Join-Path $repoRoot 'build\publish'
    $issFile = Join-Path $repoRoot 'installer\JsjmZip.iss'
    $distDir = Join-Path $repoRoot 'dist'

    if (-not $Version) {
        [xml]$xml = Get-Content $csproj -Encoding UTF8
        $Version = ($xml.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
    }
    if (-not $Version) { throw '未能从 csproj 读取版本号，请通过 -Version 指定' }
    Write-BuildLog "开始构建安装包，版本 $Version"

    if (-not $SkipPublish) {
        if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
        Write-BuildLog '正在执行 dotnet publish（win-x64 自包含）…'
        & dotnet publish $csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $publishDir
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败，退出码 $LASTEXITCODE" }
    } else {
        Write-BuildLog '已跳过发布步骤'
    }

    if (-not (Test-Path (Join-Path $publishDir 'JsjmZip.exe'))) { throw "发布目录缺少 JsjmZip.exe：$publishDir" }

    $iscc = $env:INNO_SETUP_ISCC
    if (-not $iscc -or -not (Test-Path $iscc)) {
        $candidates = @(
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
        )
        $iscc = $null
        foreach ($candidate in $candidates) {
            if ($candidate -and (Test-Path $candidate)) { $iscc = $candidate; break }
        }
        if (-not $iscc) {
            $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
            if ($cmd) { $iscc = $cmd.Source }
        }
    }
    if (-not $iscc) {
        throw '未找到 ISCC.exe，请先安装 Inno Setup 6（winget install JRSoftware.InnoSetup），或设置环境变量 INNO_SETUP_ISCC'
    }
    Write-BuildLog "使用编译器：$iscc"

    if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Force -Path $distDir | Out-Null }

    Write-BuildLog '正在编译安装包…'
    & $iscc "/DMyAppVersion=$Version" $issFile
    if ($LASTEXITCODE -ne 0) { throw "ISCC 编译失败，退出码 $LASTEXITCODE" }

    $setup = Join-Path $distDir "JsjmZip-$Version-Setup.exe"
    if (-not (Test-Path $setup)) { throw "未生成安装包：$setup" }

    $sizeMb = [Math]::Round((Get-Item $setup).Length / 1MB, 1)
    Write-BuildLog "构建完成：$setup（$sizeMb MB）"
} catch {
    Write-BuildLog ("构建失败：" + $_.Exception.Message)
    exit 1
}
