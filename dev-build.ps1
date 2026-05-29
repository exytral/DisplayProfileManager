param(
    [string]$Configuration = "Release",
    [string]$Platform      = "x64"
)

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vsRoot  = & $vswhere -latest -requires Microsoft.Component.MSBuild -property installationPath
$msbuild = Join-Path $vsRoot "MSBuild\Current\Bin\MSBuild.exe"
$sln     = "$PSScriptRoot\DisplayProfileManager.sln"
$exe     = "$PSScriptRoot\DisplayProfileManager\bin\$Platform\$Configuration\DisplayProfileManager.exe"
$proj    = "$PSScriptRoot\DisplayProfileManager\DisplayProfileManager.csproj"

# Kill any running dev instance and wait for file locks to release
$devProcs = Get-CimInstance Win32_Process -Filter "Name = 'DisplayProfileManager.exe' AND CommandLine LIKE '%--dev%'"
if ($devProcs) {
    foreach ($p in $devProcs) {
        Write-Host "Killing dev instance (PID $($p.ProcessId))..." -ForegroundColor Yellow
        Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue

        $timeout = 30
        while ((Get-Process -Id $p.ProcessId -ErrorAction SilentlyContinue) -and ($timeout -gt 0)) {
            Start-Sleep -Milliseconds 100
            $timeout--
        }
    }
}

# Restore NuGet packages for the solution via MSBuild
Write-Host "Restoring NuGet packages..." -ForegroundColor Cyan
& $msbuild $sln /t:Restore /p:Configuration=$Configuration /p:Platform=$Platform /v:minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host "Restore failed." -ForegroundColor Red
    Start-Sleep -Seconds 5
    exit 1
}

# Build using solution target filter to resolve dependencies while skipping tests
Write-Host "Building $Configuration $Platform..." -ForegroundColor Cyan
& $msbuild $sln /t:DisplayProfileManager /p:Configuration=$Configuration /p:Platform=$Platform /v:minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed." -ForegroundColor Red
    Start-Sleep -Seconds 5
    exit 1
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