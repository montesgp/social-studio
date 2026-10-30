[CmdletBinding()]
param(
    [string]$OutputDir = "SocialStudio.ApiService\bin\Debug\net10.0",
    [string]$CertSubject = "CN=Incoders Local Dev Code Signing",
    [switch]$CreateIfMissing = $true
)

$ErrorActionPreference = "Stop"

# MSBuild runs this script with Windows PowerShell. When the build was started from a
# PowerShell 7 session, the inherited PSModulePath lists the PowerShell 7 module folders first,
# so Windows PowerShell picks up the incompatible 7.x Microsoft.PowerShell.Security module and
# the Cert: drive never appears. Drop those folders and load the built-in module explicitly.
if ($PSVersionTable.PSEdition -eq "Desktop") {
    $env:PSModulePath = (($env:PSModulePath -split ';') |
        Where-Object { $_ -and $_ -notmatch '\\PowerShell\\|powershell_7' }) -join ';'
    Import-Module (Join-Path $PSHOME "Modules\Microsoft.PowerShell.Security\Microsoft.PowerShell.Security.psd1") -Force
}

$OutputDir = $OutputDir.Trim('"')
if ($OutputDir.EndsWith("\")) {
    $OutputDir = $OutputDir.TrimEnd("\")
}

function Ensure-CodeSigningCertificate {
    param(
        [string]$Subject,
        [bool]$Create
    )

    $cert = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Subject -eq $Subject -and $_.NotAfter -gt (Get-Date) } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1

    if (-not $cert -and $Create) {
        $cert = New-SelfSignedCertificate `
            -Type CodeSigningCert `
            -Subject $Subject `
            -CertStoreLocation "Cert:\CurrentUser\My" `
            -KeyAlgorithm RSA `
            -KeyLength 3072 `
            -HashAlgorithm SHA256 `
            -NotAfter (Get-Date).AddYears(2)
    }

    if (-not $cert) {
        throw "No code-signing certificate found for subject '$Subject'."
    }

    return $cert
}

function Add-CertificateToTrustStores {
    param(
        [System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate
    )

    foreach ($storeName in @("Root", "TrustedPublisher")) {
        $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($storeName, "CurrentUser")
        try {
            $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
            $existing = $store.Certificates | Where-Object Thumbprint -eq $Certificate.Thumbprint
            if (-not $existing) {
                $store.Add($Certificate)
            }
        }
        finally {
            $store.Close()
        }
    }
}

$resolvedOutput = Resolve-Path -Path $OutputDir -ErrorAction Stop
$cert = Ensure-CodeSigningCertificate -Subject $CertSubject -Create:$CreateIfMissing
Add-CertificateToTrustStores -Certificate $cert

$targets = Get-ChildItem -Path $resolvedOutput -File |
    Where-Object { $_.Extension -in @(".exe", ".dll") -and $_.Name -like "SocialStudio.*" }

if (-not $targets) {
    throw "No SocialStudio binaries found in '$resolvedOutput'."
}

Write-Host "Using certificate thumbprint: $($cert.Thumbprint)"
Write-Host "Signing binaries in: $resolvedOutput"

foreach ($target in $targets) {
    $null = Set-AuthenticodeSignature -FilePath $target.FullName -Certificate $cert -HashAlgorithm SHA256
    $signature = Get-AuthenticodeSignature -FilePath $target.FullName
    Write-Host "$($target.Name) => $($signature.Status)"
}
