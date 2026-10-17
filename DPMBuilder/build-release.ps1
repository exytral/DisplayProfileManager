# Release-only PyInstaller freeze. No system PATH or Python installation changes.
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("DPMShortcutBuilder", "DPMThemeBuilder")]
    [string]$Builder,
    [string]$PythonExe
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
if (-not $PythonExe) {
    if ($env:pythonLocation -and (Test-Path (Join-Path $env:pythonLocation "python.exe"))) {
        $PythonExe = Join-Path $env:pythonLocation "python.exe"
    }
    else {
        $PythonExe = (Get-Command python.exe -ErrorAction Stop).Source
    }
}
$PythonExe = (Resolve-Path -LiteralPath $PythonExe).Path
$version = & $PythonExe -c "import sys; print('.'.join(map(str, sys.version_info[:3])))"
if ($LASTEXITCODE -ne 0 -or $version -ne "3.13.15") {
    throw "Release Builder requires Python 3.13.15, got '$version'"
}
$basePython = & $PythonExe -c "import sys; print(sys.base_prefix)"
if ($LASTEXITCODE -ne 0 -or -not $basePython) { throw "Cannot locate base Python" }
$pythonHome = Split-Path -Parent $PythonExe
$systemRoot = $env:SystemRoot
if (-not $systemRoot) { throw "SystemRoot is missing" }
# Only this child build session sees the bounded PATH. Include native system and
# base Python DLL locations, not unrelated installed applications.
$buildPath = @(
    $pythonHome,
    (Join-Path $pythonHome "Scripts"),
    $basePython,
    (Join-Path $basePython "DLLs"),
    (Join-Path $systemRoot "System32"),
    $systemRoot
) | Where-Object { Test-Path $_ } | Select-Object -Unique
$previousPath = $env:PATH
$previousLocation = Get-Location
$outside = Join-Path $env:TEMP "dpm-2.3-builder-release"
$work = Join-Path $outside "work"
$spec = Join-Path $outside "spec"
$dist = Join-Path $outside "dist"
try {
    $env:PATH = $buildPath -join [IO.Path]::PathSeparator
    New-Item -ItemType Directory -Force -Path $work, $spec, $dist | Out-Null
    Set-Location $PSScriptRoot
    $args = @(
        "-m", "PyInstaller", "--clean", "--noconfirm", "--onefile", "--windowed",
        "--icon=$(Join-Path $PSScriptRoot 'icon.ico')", "--name=$Builder",
        "--workpath=$work", "--specpath=$spec", "--distpath=$dist"
    )
    if ($Builder -eq "DPMShortcutBuilder") {
        $args += @(
            "--exclude-module=win32ui",
            "--exclude-module=pythonwin",
            "--exclude-module=pywin",
            "--exclude-module=pywin.dialogs",
            "--exclude-module=pywin.mfc"
        )
    }
    $args += "$Builder.pyw"
    & $PythonExe @args
    if ($LASTEXITCODE -ne 0) { throw "$Builder PyInstaller freeze failed: $LASTEXITCODE" }
    $built = Join-Path $dist "$Builder.exe"
    if (-not (Test-Path $built)) { throw "Missing frozen executable: $built" }
    $packagingDist = Join-Path $PSScriptRoot "dist"
    New-Item -ItemType Directory -Force -Path $packagingDist | Out-Null
    Copy-Item -LiteralPath $built -Destination (Join-Path $packagingDist "$Builder.exe") -Force
}
finally {
    $env:PATH = $previousPath
    Set-Location $previousLocation
}
