#Requires -Version 7
<#
.SYNOPSIS
    Builds the signed Clipo MSIX package (decision 0025).

.DESCRIPTION
    Runs `dotnet build` of the app as an MSIX package, signs the .msix with
    signtool and writes it to OutputDirectory with a .sha256 file. Run
    `cargo xtask package windows` from core/ first.

    Signing is chosen by two environment variables:
      CLIPO_SIGN_PFX           path of a code-signing certificate (.pfx).
      CLIPO_SIGN_PFX_PASSWORD  its password.
    Without them the script makes a throwaway self-signed certificate whose
    subject is the manifest's Publisher. The package is then named
    Clipo-<version>-<platform>-selfsigned.msix and the certificate is written
    next to it as Clipo-test-signing.cer: a PC trusts the package only after
    that certificate is imported into Trusted People (see decision 0025).

    Other variables:
      CLIPO_EXPECT_VERSION     fail unless the manifest's version starts with this (for example 0.1.0).
      CLIPO_TIMESTAMP_URL      timestamp server for a real certificate; default http://timestamp.digicert.com.

.PARAMETER OutputDirectory
    Where the package, its checksum and, if self-signed, the certificate go.

.PARAMETER Platform
    x64 (default) or ARM64. The core has to be packaged for the same architecture.
#>
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet('x64', 'ARM64')][string]$Platform = 'x64'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$windows = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $windows 'Clipo/Package.appxmanifest'
$project = Join-Path $windows 'Clipo/Clipo.csproj'

[xml]$manifest = Get-Content -LiteralPath $manifestPath
$version = $manifest.Package.Identity.Version
$publisher = $manifest.Package.Identity.Publisher
$shortVersion = ($version -split '\.')[0..2] -join '.'

if ($env:CLIPO_EXPECT_VERSION -and $shortVersion -ne $env:CLIPO_EXPECT_VERSION) {
    throw "The manifest's version is $version, but the tag is for $($env:CLIPO_EXPECT_VERSION). Change Version in windows/Clipo/Package.appxmanifest."
}

function Find-SignTool {
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
    $found = Get-ChildItem -Path $kits -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '[\\/]x64[\\/]' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if (-not $found) {
        # The build tools package the app build restored carries one too.
        $nuget = Join-Path ($env:NUGET_PACKAGES ?? (Join-Path $HOME '.nuget/packages')) 'microsoft.windows.sdk.buildtools'
        $found = Get-ChildItem -Path $nuget -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '[\\/]x64[\\/]' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
    }
    if (-not $found) { throw 'signtool.exe was not found (Windows SDK).' }
    $found.FullName
}

$work = Join-Path ([IO.Path]::GetTempPath()) "clipo-msix-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work | Out-Null
$selfSigned = $null

try {
    # Which certificate signs. The key never goes into the repository or the artifact.
    if ($env:CLIPO_SIGN_PFX) {
        $pfx = $env:CLIPO_SIGN_PFX
        $password = $env:CLIPO_SIGN_PFX_PASSWORD
        $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($pfx, $password)
        $normalize = { param($name) ($name -replace ',\s+', ',').ToLowerInvariant() }
        if ((& $normalize $certificate.Subject) -ne (& $normalize $publisher)) {
            throw "The certificate's subject is '$($certificate.Subject)', but Publisher in Package.appxmanifest is '$publisher'. They have to be equal."
        }
        $thumbprint = $certificate.Thumbprint
    }
    else {
        $password = [Guid]::NewGuid().ToString('N')
        $selfSigned = New-SelfSignedCertificate -Type Custom -Subject $publisher -FriendlyName 'Clipo test signing' `
            -KeyUsage DigitalSignature -CertStoreLocation 'Cert:/CurrentUser/My' -NotAfter (Get-Date).AddYears(1) `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
        $pfx = Join-Path $work 'signing.pfx'
        Export-PfxCertificate -Cert $selfSigned -FilePath $pfx -Password (ConvertTo-SecureString $password -AsPlainText -Force) | Out-Null
        $thumbprint = $selfSigned.Thumbprint
    }

    # Unsigned build; signtool signs it afterwards, so signing can change (for
    # example to a cloud service) without touching the build.
    $packages = Join-Path $work 'packages'
    $rid = if ($Platform -eq 'x64') { 'win-x64' } else { 'win-arm64' }
    dotnet build $project -c Release -r $rid --self-contained `
        "-p:Platform=$Platform" '-p:WindowsPackageType=MSIX' '-p:GenerateAppxPackageOnBuild=true' `
        '-p:AppxPackageSigningEnabled=false' '-p:AppxBundle=Never' '-p:UapAppxPackageBuildMode=SideloadOnly' `
        "-p:AppxPackageDir=$packages/"
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with $LASTEXITCODE" }

    $built = @(Get-ChildItem -Path $packages -Recurse -Filter *.msix)
    if ($built.Count -ne 1) { throw "Expected one .msix under $packages, found $($built.Count)." }

    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
    $suffix = if ($selfSigned) { '-selfsigned' } else { '' }
    $msix = Join-Path $OutputDirectory "Clipo-$shortVersion-$($Platform.ToLowerInvariant())$suffix.msix"
    Copy-Item -LiteralPath $built[0].FullName -Destination $msix -Force

    $signArguments = @('sign', '/fd', 'SHA256', '/f', $pfx, '/p', $password)
    if (-not $selfSigned) {
        $signArguments += @('/tr', ($env:CLIPO_TIMESTAMP_URL ?? 'http://timestamp.digicert.com'), '/td', 'SHA256')
    }
    & (Find-SignTool) @signArguments $msix
    if ($LASTEXITCODE -ne 0) { throw "signtool failed with $LASTEXITCODE" }

    $signature = Get-AuthenticodeSignature -LiteralPath $msix
    if ($signature.SignerCertificate.Thumbprint -ne $thumbprint) {
        throw 'The package is not signed with the expected certificate.'
    }

    if ($selfSigned) {
        Export-Certificate -Cert $selfSigned -FilePath (Join-Path $OutputDirectory 'Clipo-test-signing.cer') | Out-Null
    }
    $hash = (Get-FileHash -LiteralPath $msix -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath "$msix.sha256" -Value "$hash  $(Split-Path -Leaf $msix)"
    Write-Host "Built $msix"
}
finally {
    if ($selfSigned) {
        Remove-Item -LiteralPath "Cert:/CurrentUser/My/$($selfSigned.Thumbprint)" -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
