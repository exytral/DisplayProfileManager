param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$Configuration = "Release",
    [string]$OutputDirectory = "dist/installers"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$iscc = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source,
    "C:\Program Files (x86)\Inno Setup 7\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 7\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

$msbuild = (Get-Command msbuild.exe -ErrorAction SilentlyContinue).Source
if (-not $msbuild) {
    $vswhere = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
    }
}

$iss = Join-Path $repo "DisplayProfileManager\DisplayProfileManager.Setup.iss"
$bootstrapProject = Join-Path $PSScriptRoot "DisplayProfileManager.SetupBootstrap.vcxproj"
$testsProject = Join-Path $repo "DisplayProfileManager.SetupBootstrap.Tests\DisplayProfileManager.SetupBootstrap.Tests.vcxproj"
$embeddedPayload = Join-Path $PSScriptRoot "EmbeddedPayload.exe"
$payloadDirectory = Join-Path $repo "dist\setup-payloads"
$outputDirectoryFull = Join-Path $repo $OutputDirectory
$versionValue = $Version.TrimStart("v")
if ($versionValue -notmatch '^(?<Major>\d+)\.(?<Minor>\d+)\.(?<Build>\d+)(?:\.(?<Revision>\d+))?$') {
    throw "Version must be a numeric Windows version in major.minor.build[.revision] form."
}
$versionMajor = [int]$Matches.Major
$versionMinor = [int]$Matches.Minor
$versionBuild = [int]$Matches.Build
$versionRevision = if ($Matches.Revision) { [int]$Matches.Revision } else { 0 }
foreach ($component in @($versionMajor, $versionMinor, $versionBuild, $versionRevision)) {
    if ($component -lt 0 -or $component -gt 65535) { throw "Version components must be between 0 and 65535." }
}

if (-not $msbuild) { throw "MSBuild was not found." }
New-Item -ItemType Directory -Force -Path $payloadDirectory, $outputDirectoryFull | Out-Null

$issText = Get-Content -Raw $iss
foreach ($required in @(
    "PrivilegesRequired=lowest",
    "PrivilegesRequiredOverridesAllowed=commandline",
    "UsePreviousPrivileges=no",
    "UsePreviousLanguage=no",
    "DefaultDirName={autopf}\{#MyAppName}",
    "AppId={code:GetScopeAppId}",
    "UninstallDisplayName={code:GetScopeDisplayName}",
    "UninstallFilesDir={app}\uninstall",
    "MyScopedAppIdPrefix",
    "-currentuser}",
    "-allusers}",
    "AppUserModelID: ""exytral.DisplayProfileManager"""
)) {
    if (-not $issText.Contains($required)) { throw "Installer invariant missing: $required" }
}
if ($issText.Contains("PrivilegesRequiredOverridesAllowed=dialog")) { throw "Stock Inno privilege chooser must not be enabled." }
if ($issText.Contains("AppId={#MyAppId}")) { throw "Installer must not reuse one uninstall AppId across user and machine scopes." }

$architectures = @(
    @{ Inno = "x64"; Platform = "x64"; Output = "x64" },
    @{ Inno = "x86"; Platform = "Win32"; Output = "x86" },
    @{ Inno = "arm64"; Platform = "ARM64"; Output = "arm64" }
)

foreach ($architecture in $architectures) {
    & $msbuild $testsProject /t:Rebuild /p:Configuration=$Configuration /p:Platform=$($architecture.Platform) /p:SolutionDir="$repo\"
    if ($LASTEXITCODE -ne 0) { throw "Setup bootstrap policy test build failed for $($architecture.Platform)." }
}

& (Join-Path $repo "DisplayProfileManager.SetupBootstrap.Tests\bin\x64\Release\SetupBootstrapPolicyTests.exe")
if ($LASTEXITCODE -ne 0) { throw "x64 setup bootstrap policy tests failed." }
& (Join-Path $repo "DisplayProfileManager.SetupBootstrap.Tests\bin\Win32\Release\SetupBootstrapPolicyTests.exe")
if ($LASTEXITCODE -ne 0) { throw "x86 setup bootstrap policy tests failed." }

if (-not $iscc) {
    throw "Inno Setup compiler was not found. Install Inno Setup 6 or 7, or place ISCC.exe on PATH."
}

try {
    foreach ($architecture in $architectures) {
        $payloadName = "DisplayProfileManager-$versionValue-$($architecture.Output)-InnoPayload"
        & $iscc $iss "/DTargetArch=$($architecture.Inno)" "/DExpectedTargetArch=$($architecture.Inno)" "/DMyAppVersion=$Version" "/O$payloadDirectory" "/F$payloadName"
        if ($LASTEXITCODE -ne 0) { throw "Inno payload compile failed for $($architecture.Inno)." }

        $payload = Join-Path $payloadDirectory "$payloadName.exe"
        if (-not (Test-Path $payload)) { throw "Missing internal Inno payload: $payload" }
        Copy-Item $payload $embeddedPayload -Force

        & $msbuild $bootstrapProject /t:Rebuild /p:Configuration=$Configuration /p:Platform=$($architecture.Platform) /p:SolutionDir="$repo\" /p:SetupVersion=$versionValue /p:SetupVersionMajor=$versionMajor /p:SetupVersionMinor=$versionMinor /p:SetupVersionBuild=$versionBuild /p:SetupVersionRevision=$versionRevision
        if ($LASTEXITCODE -ne 0) { throw "Setup bootstrapper build failed for $($architecture.Platform)." }

        $builtBootstrap = Join-Path $PSScriptRoot "bin\$($architecture.Platform)\Release\DisplayProfileManager.SetupBootstrap.exe"
        $publicInstaller = Join-Path $outputDirectoryFull "DisplayProfileManager-$versionValue-$($architecture.Output)-Setup.exe"
        Copy-Item $builtBootstrap $publicInstaller -Force
    }
}
finally {
    Remove-Item -Force -ErrorAction SilentlyContinue $embeddedPayload
}

$finalInstallers = $architectures | ForEach-Object {
    Join-Path $outputDirectoryFull "DisplayProfileManager-$versionValue-$($_.Output)-Setup.exe"
}
foreach ($installer in $finalInstallers) {
    if (-not (Test-Path $installer)) { throw "Missing final setup bootstrapper: $installer" }
}
if (Get-ChildItem $outputDirectoryFull -File | Where-Object { $_.Name -like "*InnoPayload*" }) {
    throw "Internal Inno payload leaked into release installer directory."
}
$hashes = $finalInstallers | ForEach-Object {
    $hash = (Get-FileHash -Algorithm SHA256 $_).Hash
    Write-Host "$_ => $hash"
    $hash
}
if (($hashes | Select-Object -Unique).Count -ne $finalInstallers.Count) {
    throw "Architecture setup bootstrapper artifacts are not distinct binaries."
}

function Get-PeMachine([string]$Path) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
        throw "$Path is not a PE file."
    }
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($peOffset -lt 0 -or $peOffset + 6 -gt $bytes.Length) {
        throw "$Path has an invalid PE header offset."
    }
    return [BitConverter]::ToUInt16($bytes, $peOffset + 4)
}

$expectedMachines = @{
    x64 = 0x8664
    x86 = 0x014c
    arm64 = 0xAA64
}
foreach ($architecture in $architectures) {
    $installer = Join-Path $outputDirectoryFull "DisplayProfileManager-$versionValue-$($architecture.Output)-Setup.exe"
    $machine = Get-PeMachine $installer
    if ($machine -ne $expectedMachines[$architecture.Output]) {
        throw ("Final setup architecture mismatch for {0}: got 0x{1:X4}" -f $architecture.Output, $machine)
    }
}

# Fail closed if native CodeView debug records leak the machine's PDB path.
$python = (Get-Command python.exe -ErrorAction Stop).Source
& $python (Join-Path $PSScriptRoot "test_pdb_path.py")
if ($LASTEXITCODE -ne 0) { throw "Setup PDB locator regression tests failed." }
& $python (Join-Path $PSScriptRoot "validate-pdb-path.py") @finalInstallers
if ($LASTEXITCODE -ne 0) { throw "Setup PDB locator validation failed." }

foreach ($installer in $finalInstallers) {
    $versionInfo = (Get-Item $installer).VersionInfo
    $metadata = @{
        CompanyName = $versionInfo.CompanyName
        FileDescription = $versionInfo.FileDescription
        ProductName = $versionInfo.ProductName
        FileVersion = $versionInfo.FileVersion
        ProductVersion = $versionInfo.ProductVersion
    }
    $expectedMetadata = @{
        CompanyName = "exytral"
        FileDescription = "Display Profile Manager Setup"
        ProductName = "Display Profile Manager"
        FileVersion = $versionValue
        ProductVersion = $versionValue
    }
    foreach ($key in $expectedMetadata.Keys) {
        if ($metadata[$key] -ne $expectedMetadata[$key]) {
            throw "Final setup metadata mismatch for $installer / ${key}: '$($metadata[$key])'"
        }
    }
}
