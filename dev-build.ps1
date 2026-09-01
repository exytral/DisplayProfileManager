param(
    [string]$Configuration = "Release",
    [string]$Platform      = "x64"
)

$vswhere  = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vsRoot   = & $vswhere -latest -requires Microsoft.Component.MSBuild -property installationPath
$msbuild  = Join-Path $vsRoot "MSBuild\Current\Bin\MSBuild.exe"
$sln      = "$PSScriptRoot\DisplayProfileManager.sln"
$exe      = "$PSScriptRoot\DisplayProfileManager\bin\$Platform\$Configuration\DisplayProfileManager.exe"
$settings = "$env:APPDATA\DisplayProfileManager\Settings.json"

# Gracefully close any running dev instance before building
$devProcs = Get-CimInstance Win32_Process -Filter "Name = 'DisplayProfileManager.exe' AND CommandLine LIKE '%--dev%'"
if ($devProcs) {
    Write-Host "Requesting dev instance shutdown..." -ForegroundColor Yellow
    & $exe --exit | Out-Null

    $timeout = 10
    while ($timeout -gt 0) {
        $devProcs = Get-CimInstance Win32_Process -Filter "Name = 'DisplayProfileManager.exe' AND CommandLine LIKE '%--dev%'"
        if (-not $devProcs) {
            break
        }

        Start-Sleep -Milliseconds 100
        $timeout--
    }

    if ($devProcs) {
        Write-Host "Dev instance did not exit gracefully; terminating remaining process..." -ForegroundColor Red

        foreach ($p in $devProcs) {
            Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
        }
    }
}

# Check whether the shell extension should be re-enabled after the build
$shellMenuWasEnabled = $false
if (Test-Path $settings) {
    try {
        $json = Get-Content $settings -Raw | ConvertFrom-Json
        $shellMenuWasEnabled = $json.desktopContextMenu -eq $true
    } catch {
        Write-Host "Could not read Settings.json - shell menu state unknown." -ForegroundColor Yellow
    }
}

# Always attempt to unregister the shell extension
Write-Host "Unregistering shell extension for build..." -ForegroundColor Cyan
& $exe --unshell | Out-Null
$unshellExitCode = $LASTEXITCODE

# Restore NuGet packages for solution via MSBuild
Write-Host "Restoring NuGet packages..." -ForegroundColor Cyan
& $msbuild $sln /t:Restore /p:Configuration=$Configuration /p:Platform=$Platform /v:minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host "Restore failed." -ForegroundColor Red
    Start-Sleep -Seconds 5
    exit 1
}

# Build DisplayProfileManager.ShellExt
Write-Host "Building DisplayProfileManager.ShellExt $Configuration $Platform..." -ForegroundColor Cyan
& $msbuild $sln /t:DisplayProfileManager_ShellExt /p:Configuration=$Configuration /p:Platform=$Platform /v:minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host "DisplayProfileManager.ShellExt build failed." -ForegroundColor Red
    Start-Sleep -Seconds 5
    exit 1
}

# Build DisplayProfileManager
Write-Host "Building DisplayProfileManager $Configuration $Platform..." -ForegroundColor Cyan
& $msbuild $sln /t:DisplayProfileManager /p:Configuration=$Configuration /p:Platform=$Platform /v:minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed." -ForegroundColor Red
    Start-Sleep -Seconds 5
    exit 1
}

# Re-register shell extension if previously enabled
if ($shellMenuWasEnabled) {
    Write-Host "Re-registering shell extension..." -ForegroundColor Cyan
    & $exe --shell | Out-Null

    if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 2) {
        Write-Host "Shell extension registration failed." -ForegroundColor Red
        Start-Sleep -Seconds 5
        exit 1
    }
}

# Launch dev instance
if (Test-Path $exe) {
    Write-Host "Launching dev instance..." -ForegroundColor Cyan
    Start-Process -FilePath $exe -ArgumentList "--dev"
} else {
    Write-Host "Error: Executable not found at $exe" -ForegroundColor Red
    Start-Sleep -Seconds 5
    exit 1
}