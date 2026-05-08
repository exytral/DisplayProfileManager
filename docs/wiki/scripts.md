# Scripts

Each profile can run one or more scripts automatically when the profile is applied — after display settings, DPI, and audio have all been committed. Use this to launch apps, control smart devices, kill background processes, or trigger anything else that should change with your display context.

---

## Supported file types

| Type | How it runs |
|---|---|
| `.ps1` | PowerShell — launched with `-ExecutionPolicy Bypass` |
| `.bat`/`.cmd` | Batch — launched via `cmd.exe /c` |
| `.py` | Python — launched via `python.exe` on `PATH` |
| `.exe` | Converted to a `.lnk` shortcut on import, launched via Windows Shell — see below |

### A note on `.exe` files

DPM does not launch `.exe` files directly. When you import an executable, DPM automatically creates a `.lnk` shortcut for it in the scripts folder and executes via the Windows Shell. This avoids COM reference issues with direct process launch. The shortcut is created transparently — you don't need to do anything differently. Arguments are still passed through normally.

---

## Scripts folder

All scripts are sandboxed to:

```
%AppData%\Roaming\DisplayProfileManager\Scripts\
```

When you import a script, it is copied into this folder automatically. Scripts must live here — references to files outside this folder are not supported.

To delete a script file from the sandbox, click **Open Folder** in the main window, navigate to the `Scripts\` subfolder, and delete the file there. This does not remove it from any profiles that reference it — you'll need to remove it from those profiles separately.

---

## Adding a script to a profile

See [Creating and Managing Profiles — Scripts](./profiles.md#scripts) for the UI walkthrough. In brief:

1. Open the profile editor and scroll to **Scripts**.
2. Click **Import** and select your script file.
3. Optionally type arguments in the field next to the script entry.

The **Enable** toggle in the Scripts section header controls whether any scripts run for this profile. There is no per-script toggle.

---

## Arguments

Arguments are appended after the script when DPM launches it:

- `.ps1`: `powershell.exe -ExecutionPolicy Bypass -File "script.ps1" <args>`
- `.bat`/`.cmd`: `cmd.exe /c "script.bat" <args>`
- `.py`: `python.exe "script.py" <args>`
- `.exe`/`.lnk`: executed via Windows Shell with args passed through

---

## Examples

### Launch an application on profile apply

```bat
@echo off
start "" "C:\Program Files\MyApp\MyApp.exe"
```

Save as `launch-myapp.bat` and add to your profile. Runs silently when the profile is applied.

---

### Kill a process when switching profiles

```powershell
Stop-Process -Name "MyApp" -ErrorAction SilentlyContinue
```

Save as `kill-myapp.ps1` and add to the profile you switch *to* when leaving that setup.

---

### Launch Steam Big Picture Mode on profile apply

Use this as a profile script when you want switching to a TV profile to also open Big Picture Mode immediately — for example, when you sit down at a couch setup and want everything to start at once.

```powershell
Start-Process "steam://open/bigpicture"
```

Save as `launch-bigpicture.ps1` and add it to your TV profile. Steam must already be running.

> For the reverse — automatically switching profiles *when* Big Picture Mode opens or closes rather than triggering it manually — see [CLI Reference — Steam Big Picture Mode watcher](./cli.md#steam-big-picture-mode-watcher).

---

### Control a Samsung Smart TV via Home Assistant

`ha-smarttv-switch.ps1` controls a Samsung Smart TV through the Home Assistant REST API. It accepts `on` or `off` as an argument (defaults to `off` if none is given).

The script controls two HA entities simultaneously:
- `media_player.samsung_smarttv` — powers the TV on or off
- `input_boolean.samsung_smarttv` — a helper boolean that tracks the intended TV state

The boolean enables HA to handle PC power events. Although HA can track both the PC and TV's state, HA does not know whether the TV should always turn on with the PC — DPM only runs the script at profile apply time. The boolean records the intended state: when it's on, a separate HA automation turns on the TV when it detects PC activity. Without it, the TV stays off between sleep and wake events.

```powershell
[CmdletBinding(DefaultParameterSetName = "Positional")]
param(
    [Parameter(ParameterSetName = "Positional", Position = 0)]
    [ValidateSet("on", "off")]
    [string]$State = "off",

    [Parameter(ParameterSetName = "OnSwitch")]
    [switch]$On,

    [Parameter(ParameterSetName = "OffSwitch")]
    [switch]$Off
)

# Read the HA token silently from Windows Credential Manager (no plaintext secrets)
$code = @"
using System;
using System.Runtime.InteropServices;

public class CredInterop {
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CredRead(string target, int type, int reserved, out IntPtr credentialPtr);

    public static string GetPassword(string target) {
        IntPtr authPtr;
        if (CredRead(target, 1, 0, out authPtr)) {
            var cred = (Credential)Marshal.PtrToStructure(authPtr, typeof(Credential));
            return Marshal.PtrToStringUni(cred.CredentialBlob);
        }
        return null;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential {
        public int Flags; public int Type; public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize; public IntPtr CredentialBlob;
        public int Persist; public int AttributeCount; public IntPtr Attributes;
        public string TargetAlias; public string UserName;
    }
}
"@

if (-not ([System.Management.Automation.PSTypeName]"CredInterop").Type) {
    Add-Type -TypeDefinition $code
}

if ($On)       { $State = "on" }
elseif ($Off)  { $State = "off" }

$token = [CredInterop]::GetPassword("HomeAssistantToken")
if ([string]::IsNullOrWhiteSpace($token)) {
    Write-Error "Could not find 'HomeAssistantToken' in Credential Manager."
    exit 1
}

$haUrl      = "http://homeassistant.local:8123"
$tvEntity   = "media_player.samsung_smarttv"
$boolEntity = "input_boolean.samsung_smarttv"
$headers    = @{ "Authorization" = "Bearer $token"; "Content-Type" = "application/json" }

function Send-HARequest {
    param($Domain, $EntityId, $State)
    $body = @{ entity_id = $EntityId } | ConvertTo-Json
    try {
        Invoke-RestMethod -Method Post -Uri "$haUrl/api/services/$Domain/turn_$State" `
            -Headers $headers -Body $body -ErrorAction Stop -TimeoutSec 5
        Write-Host "Success: $State -> $EntityId" -ForegroundColor Green
    } catch {
        Write-Error "Failed for ${EntityId}: $($_.Exception.Message)"
    }
}

Send-HARequest -Domain "media_player"  -EntityId $tvEntity   -State $State
Send-HARequest -Domain "input_boolean" -EntityId $boolEntity -State $State
```

**Setup:**

1. In HA, create an `input_boolean` helper named `samsung_smarttv` (Settings → Devices & Services → Helpers).
2. Create an HA automation: when PC changes to `on` AND if `input_boolean.samsung_smarttv` is `on`, call `media_player.turn_on` for your TV. This handles wake-from-sleep.
3. Store your Long-Lived Access Token in Windows Credential Manager: open **Credential Manager → Windows Credentials → Add a generic credential**. Set the name to `HomeAssistantToken`, leave username blank, paste your token as the password.
4. Update `$haUrl`, `$tvEntity`, and `$boolEntity` to match your setup.
5. Add the script to your TV profile with argument `on`, and to your desktop profile with argument `off`.

---

### Control an LG TV via LGTV Companion

`lg-tv-switch.ps1` powers an LG WebOS TV on or off using [LGTV Companion](https://github.com/JPersson77/LGTVCompanion)'s `LGTVcli.exe`. Defaults to `off` if no argument is given.

Unlike the Samsung Smart TV + HA approach, you don't need a state boolean — LGTV Companion runs as a background service and handles sleep/resume automatically, powering the TV on when your PC wakes and off when it sleeps. This script is only needed for explicit profile-triggered switching (e.g. switching to a work profile that doesn't use the TV).

```powershell
param(
    [Parameter(Position = 0)]
    [ValidateSet("on", "off")]
    [string]$State = "off"
)

$cli = "C:\Program Files\LGTV Companion\LGTVcli.exe"

if (-not (Test-Path $cli)) {
    Write-Error "LGTVcli.exe not found. Install LGTV Companion from https://github.com/JPersson77/LGTVCompanion"
    exit 1
}

$arg = if ($State -eq "on") { "-poweron" } else { "-poweroff" }

try {
    & $cli $arg
    Write-Host "Success: sent '$arg' to LG TV" -ForegroundColor Green
} catch {
    Write-Error "Failed to call LGTVcli.exe: $($_.Exception.Message)"
}
```

**Setup:**

1. Install [LGTV Companion](https://github.com/JPersson77/LGTVCompanion) and configure your TV in its settings (IP address, MAC address). Run through its setup wizard to pair.
2. Confirm `LGTVcli.exe` is at `C:\Program Files\LGTV Companion\` or update the path in the script.
3. Add the script to your TV profile with argument `on`, and to your desktop profile with argument `off`.

> **Note:** LGTV Companion requires the TV to be on the same subnet as your PC (Wake-on-LAN is layer 2 only). Ensure "Turn on via WiFi" is enabled in your TV's network settings regardless of whether you use Wi-Fi or Ethernet.
