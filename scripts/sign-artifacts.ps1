<#
.SYNOPSIS
    使用 MZip 代码签名证书为可执行文件签名并验证。
.DESCRIPTION
    优先使用当前用户证书库中带私钥的代码签名证书；缺失时自动从 certs\MZip-CodeSigning.pfx 导入。
    先尝试 RFC3161 时间戳，网络不可用时退化为无时间戳签名（自签名证书本机信任不受影响）。
.PARAMETER Path
    待签名的文件（可多个），通常为 MZip.exe 与安装包。
.PARAMETER PfxPath
    PFX 证书路径，默认 certs\MZip-CodeSigning.pfx。
.PARAMETER Password
    PFX 密码，默认 123456。
.PARAMETER TimestampUrl
    时间戳服务地址，默认 http://timestamp.digicert.com。
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\sign-artifacts.ps1 -Path dist\MZip-1.0.0-Setup.exe
#>
param(
    [Parameter(Mandatory = $true)]
    [string[]]$Path,
    [string]$PfxPath,
    [string]$Password = '123456',
    [string]$Subject = 'CN=MZip Development, O=MZip, C=CN',
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $PfxPath) { $PfxPath = Join-Path $repoRoot 'certs\MZip-CodeSigning.pfx' }

function Get-SigningCertificate {
    $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
        Where-Object { $_.Subject -eq $Subject -and $_.HasPrivateKey } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1
    if ($cert) { return $cert }

    if (-not (Test-Path $PfxPath)) {
        throw "未找到代码签名证书。请先运行 scripts\setup-codesign-cert.ps1 生成证书。"
    }
    Write-Host "从 PFX 导入签名证书：$PfxPath"
    $securePassword = ConvertTo-SecureString -String $Password -AsPlainText -Force
    Import-PfxCertificate -FilePath $PfxPath -CertStoreLocation 'Cert:\CurrentUser\My' -Password $securePassword | Out-Null
    $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
        Where-Object { $_.Subject -eq $Subject -and $_.HasPrivateKey } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1
    if (-not $cert) { throw '导入 PFX 后仍未找到带私钥的代码签名证书。' }
    return $cert
}

$cert = Get-SigningCertificate
Write-Host "使用证书：$($cert.Subject)（$($cert.Thumbprint)）"

$failed = @()
foreach ($item in $Path) {
    $file = [IO.Path]::GetFullPath($item)
    if (-not (Test-Path $file)) {
        Write-Warning "文件不存在，跳过：$file"
        $failed += $file
        continue
    }

    Write-Host "正在签名：$file"
    try {
        $result = Set-AuthenticodeSignature -FilePath $file -Certificate $cert -TimestampServer $TimestampUrl -ErrorAction Stop
    } catch {
        Write-Warning ("时间戳签名失败，改为无时间戳签名：" + $_.Exception.Message)
        $result = Set-AuthenticodeSignature -FilePath $file -Certificate $cert -ErrorAction Stop
    }

    if ($result.Status -ne 'Valid') {
        $failed += $file
        Write-Warning ("签名状态异常：" + $result.Status + " " + $result.StatusMessage)
    } else {
        Write-Host ("签名成功：" + (Split-Path $file -Leaf))
    }
}

if ($failed.Count -gt 0) {
    throw ("以下文件签名失败：" + ($failed -join '; '))
}

Write-Host '全部文件签名完成。'
