<#
.SYNOPSIS
    生成并安装 MZip 自签名代码签名证书。
.DESCRIPTION
    1. 在当前用户"个人"证书库创建代码签名证书（默认 CN=MZip Development，有效期 5 年）
    2. 导出 PFX（含私钥）与 CER（公钥）到 certs 目录
    3. 将 CER 导入"受信任的根证书颁发机构"和"受信任的发布者"，使本机签名验证通过
.PARAMETER Force
    已存在同主题证书时强制重新生成
.PARAMETER Password
    PFX 导出密码（按项目规则明文保存，默认 123456）
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\setup-codesign-cert.ps1
#>
param(
    [string]$Subject = 'CN=MZip Development, O=MZip, C=CN',
    [string]$Password = '123456',
    [int]$Years = 5,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$certDir = Join-Path $repoRoot 'certs'
$pfxPath = Join-Path $certDir 'MZip-CodeSigning.pfx'
$cerPath = Join-Path $certDir 'MZip-CodeSigning.cer'

if (-not (Test-Path $certDir)) { New-Item -ItemType Directory -Force -Path $certDir | Out-Null }

$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
    Where-Object { $_.Subject -eq $Subject -and $_.HasPrivateKey } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if ($cert -and $Force) {
    Write-Host "移除已有证书：$($cert.Thumbprint)"
    Remove-Item -Path ("Cert:\CurrentUser\My\" + $cert.Thumbprint) -Force
    $cert = $null
}

if (-not $cert) {
    Write-Host "正在创建代码签名证书：$Subject"
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $Subject `
        -KeyUsage DigitalSignature `
        -KeySpec Signature `
        -KeyExportPolicy Exportable `
        -KeyLength 2048 `
        -HashAlgorithm SHA256 `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -NotAfter (Get-Date).AddYears($Years)
    Write-Host "证书已创建，指纹：$($cert.Thumbprint)"
} else {
    Write-Host "已存在可用的代码签名证书，指纹：$($cert.Thumbprint)"
}

$securePassword = ConvertTo-SecureString -String $Password -AsPlainText -Force
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePassword -Force | Out-Null
Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null
Write-Host "已导出：$pfxPath"
Write-Host "已导出：$cerPath"

# 导入到受信任根与受信任发布者（当前用户级，无需管理员权限）
foreach ($store in @('Root', 'TrustedPublisher')) {
    $storePath = "Cert:\CurrentUser\$store"
    $exists = Get-ChildItem $storePath | Where-Object { $_.Thumbprint -eq $cert.Thumbprint }
    if ($exists) {
        Write-Host "证书已存在于 $store，跳过导入"
    } else {
        Import-Certificate -FilePath $cerPath -CertStoreLocation $storePath | Out-Null
        Write-Host "已导入证书到 $store"
    }
}

Write-Host ''
Write-Host '证书配置完成：'
Write-Host ("  主题      : " + $cert.Subject)
Write-Host ("  指纹      : " + $cert.Thumbprint)
Write-Host ("  有效期至  : " + $cert.NotAfter.ToString('yyyy-MM-dd'))
Write-Host ("  PFX 文件  : " + $pfxPath)
Write-Host ("  PFX 密码  : " + $Password)
