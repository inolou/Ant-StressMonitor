# Ant-StressMonitor — Runbook

Step-by-step instructions for getting the overlay up and running from scratch, operating it, and resolving common problems.

---

## Contents

1. [Hardware you need](#1-hardware-you-need)
2. [Software prerequisites](#2-software-prerequisites)
3. [Install the ANT+ USB driver](#3-install-the-ant-usb-driver)
4. [Get the application](#4-get-the-application)
   - [Option A — Download a pre-built release](#option-a-download-a-pre-built-release)
   - [Option B — Build from source](#option-b-build-from-source)
5. [First run](#5-first-run)
6. [Position the overlay on the VS status bar](#6-position-the-overlay-on-the-vs-status-bar)
7. [Understanding what you see](#7-understanding-what-you-see)
8. [Daily use](#8-daily-use)
9. [Troubleshooting](#9-troubleshooting)
10. [Closing / uninstalling](#10-closing-uninstalling)

---

## 1. Hardware you need

| Item | Notes |
|---|---|
| Windows 10 or 11 laptop / desktop | 64-bit OS is fine; the app runs in 32-bit (x86) mode |
| **Garmin/Dynastream ANT+ USB stick** | USB1 (old square stick) or USB2 (slim stick) both work. Search "ANT USB Stick 2" on Amazon. |
| **ANT+ heart rate monitor** | Any chest strap or optical sensor branded ANT+ (Garmin, Wahoo, Polar H7/H9/H10, etc.) |

---

## 2. Software prerequisites

### Windows — always required

- Windows 10 version 1903 or later, or Windows 11.
- **No .NET runtime installation required** — the published `.exe` is self-contained.

### .NET 8 SDK — only needed if building from source

1. Open [https://dotnet.microsoft.com/download/dotnet/8.0](https://dotnet.microsoft.com/download/dotnet/8.0).
2. Under **.NET 8.0 SDK**, click **Download x64 Installer** (works on 64-bit Windows).
3. Run the installer and accept all defaults.
4. Open a new **Command Prompt** or **PowerShell** window and verify:
   ```powershell
   dotnet --version
   # Expected: 8.x.xxx
   ```

---

## 3. Install the ANT+ USB driver

The ANT USB stick requires a Silicon Labs CP210x USB-to-UART driver. The driver files ship inside the NuGet package used by this app, but you must install the system driver separately **once** per machine.

### Step-by-step

1. Download the **CP210x Windows Drivers** from Silicon Labs:  
   [https://www.silabs.com/developers/usb-to-uart-bridge-vcp-drivers](https://www.silabs.com/developers/usb-to-uart-bridge-vcp-drivers)  
   → Click **Downloads** → **CP210x Windows Drivers**.

2. Extract the ZIP and run **CP210xVCPInstaller_x64.exe** (on 64-bit Windows).

3. Plug in your ANT+ USB stick.

4. Open **Device Manager** (`Win + X` → Device Manager).  
   Under **Ports (COM & LPT)** you should see **Silicon Labs CP210x USB to UART Bridge (COMx)**.  
   If it shows a yellow warning triangle, right-click → **Update driver** → **Browse my computer** → point to the extracted driver folder.

5. Leave the USB stick plugged in.

> **Already have Garmin Express or ANT Agent installed?** The driver is likely already present. Skip to [Step 4](#4-get-the-application).

---

## 4. Get the application

### Option A — Download a pre-built release

> ✅ Recommended for most users. No .NET SDK needed.

1. Go to the [Releases page](../../releases).
2. Download **`AntStressMonitor-win-x86.zip`** from the latest release.
3. Extract the ZIP to a folder of your choice, e.g. `C:\Tools\AntStressMonitor\`.  
   After extraction the folder must contain **all four files**:
   ```
   AntStressMonitor.exe
   ANT_WrappedLib.dll
   DSI_CP210xManufacturing_3_1.dll
   DSI_SiUSBXp_3_1.dll
   ```
   > ⚠️ **Do not move the `.exe` without also moving the three `.dll` files.** They must stay in the same directory.

4. Optionally right-click `AntStressMonitor.exe` → **Create shortcut** and place it on your Desktop or in your Start Menu for easy access.

---

### Option B — Build from source

> Requires the .NET 8 SDK (see [Step 2](#2-software-prerequisites)).

1. **Clone the repository**
   ```powershell
   git clone https://github.com/inolou/Ant-StressMonitor.git
   cd Ant-StressMonitor
   ```

2. **Restore NuGet packages** (optional — `publish` does this automatically)
   ```powershell
   dotnet restore AntStressMonitor/AntStressMonitor.csproj
   ```

3. **Publish a self-contained single-file executable**
   ```powershell
   dotnet publish AntStressMonitor/AntStressMonitor.csproj -c Release
   ```

4. **Locate the output** — the publish folder is printed at the end of the command, e.g.:
   ```
   AntStressMonitor → ...\bin\Release\net8.0-windows\win-x86\publish\
   ```
   Navigate to that folder. You should see:
   ```
   AntStressMonitor.exe          (~145 MB — self-contained runtime + app)
   ANT_WrappedLib.dll
   DSI_CP210xManufacturing_3_1.dll
   DSI_SiUSBXp_3_1.dll
   AntStressMonitor.pdb          (debug symbols — optional, safe to delete)
   ```

5. Copy the entire publish folder to a permanent location (e.g. `C:\Tools\AntStressMonitor\`).

---

## 5. First run

1. **Wear your heart rate monitor** — put on the chest strap or wrist/arm sensor and make sure it has made skin contact (sensors need moisture to activate).

2. **Ensure the ANT+ USB stick is plugged in** — you should hear the Windows "device connected" chime.

3. **Launch the app** — double-click `AntStressMonitor.exe`.

4. The overlay appears as a small `#007ACC` blue bar (150 × 20 px) near the **top-left corner** of your primary monitor.

5. Initial display states:

   | Text shown | Meaning |
   |---|---|
   | `No ANT+` (white) | USB stick not detected — check the driver ([Step 3](#3-install-the-ant-usb-driver)) |
   | `-- BPM` (white) | USB stick found; waiting for your HR monitor to broadcast |
   | `N BPM` (white) | Reading received; avg BPM is below the 113 BPM threshold |
   | `N BPM` (green) | Avg BPM is at or above 113 — you are in your target zone |

6. Within **5–15 seconds** of the app starting you should see a real BPM number.  
   If `-- BPM` persists for more than 30 seconds:
   - Move the USB stick to a USB port that is physically closer to your body.
   - Check that the heart rate monitor is active (some require pressing a button or getting wet).

---

## 6. Position the overlay on the VS status bar

The goal is to make the overlay look like a native part of the Visual Studio status bar.

1. Open Visual Studio (or any window whose status bar you want to use as the backdrop).
2. Click and hold the overlay bar with your **left mouse button**.
3. Drag it to the right side of the VS status bar — the colors should blend seamlessly because both use `#007ACC`.
4. Release the mouse button.

The overlay is always-on-top, so it will remain visible even when other windows gain focus.

> **Tip:** Position it over a quiet area of the status bar (e.g. the far-right side past the line/column indicator) to avoid covering useful VS information.

---

## 7. Understanding what you see

```
┌──────────────────────────────────────────────────────────────────────┐
│  127 BPM                                                       4:32  │
│  ▲ Rolling 1-min average BPM                    Time in Zone ▲       │
│  Green if ≥ 113, white otherwise                M:SS format          │
└──────────────────────────────────────────────────────────────────────┘
```

### BPM number (left side)

- Calculated as the **rolling 1-minute average** of all ANT+ heartbeat readings received in the last 60 seconds.
- Updates every time a new ANT+ message is received (typically every ~1 second).

### Time in Zone (right side)

- Counts up in `M:SS` format from the moment your average BPM first reaches or exceeds **113 BPM**.
- **Stops** (resets to `0:00`) if your average drops back below 113 BPM.
- **Auto-resets to `0:00`** after 60 continuous minutes in zone — a new zone session begins automatically.

### Threshold: 113 BPM

Calculated as **60 % of estimated maximum heart rate** for a 32-year-old:  
`(220 − 32) × 0.60 = 112.8 ≈ 113 BPM`

To adjust for a different age, edit `TargetBpmThreshold` in `MainWindow.xaml.cs` and rebuild (see [Build from source](#option-b-build-from-source)).

---

## 8. Daily use

### Start-up checklist

- [ ] Heart rate monitor is on your body and active
- [ ] ANT+ USB stick is plugged in
- [ ] Double-click `AntStressMonitor.exe`
- [ ] Drag the overlay into position on the VS status bar
- [ ] Confirm BPM number appears within ~15 seconds

### Routine

| What happens | What to do |
|---|---|
| BPM text is **white** | Keep warming up — not in target zone yet |
| BPM text turns **green** | You've crossed 113 BPM; Time-in-Zone counter starts |
| Time-in-Zone resets | 60-minute zone session completed; new session begins automatically |
| ANT+ signal drops (shows `-- BPM`) | Move USB stick closer, or check HR monitor battery |

### Stopping

- Press **Alt + F4** while the overlay has focus, **or**
- Right-click the `AntStressMonitor` process in **Task Manager** → **End task**.

---

## 9. Troubleshooting

### `No ANT+` is displayed

| Check | Action |
|---|---|
| USB stick not plugged in | Plug it in and wait 5 seconds, then relaunch |
| Driver not installed | Follow [Step 3](#3-install-the-ant-usb-driver) |
| Driver conflict | Open Device Manager → expand **Ports (COM & LPT)** — look for a yellow `!` next to the CP210x device → right-click → Update driver |
| Wrong bit-width DLL | Ensure all four files (`.exe` + three `.dll`) are in the same folder; do not mix DLLs from different releases |

### `-- BPM` never changes

| Check | Action |
|---|---|
| HR monitor not active | Chest straps need moisture — wet the electrodes and press firmly |
| HR monitor too far away | ANT+ typical range is ~3 m; move USB stick to a front USB port |
| HR monitor battery dead | Replace battery |
| Interference | Move away from other 2.4 GHz devices; try a different USB port |

### Text is blurry / too small

The app uses PerMonitorV2 DPI awareness and `SnapsToDevicePixels`, which should handle this automatically. If text is still blurry:

1. Right-click `AntStressMonitor.exe` → **Properties**.
2. Go to the **Compatibility** tab.
3. Click **Change high DPI settings**.
4. Check **Override high DPI scaling behavior** → set to **System (Enhanced)**.
5. Click OK and relaunch.

### Overlay is off-screen / not visible

1. Open **Task Manager** (`Ctrl + Shift + Esc`).
2. Find `AntStressMonitor` → **End task**.
3. Relaunch — it opens at the top-left of the primary monitor.

### App crashes immediately at launch

- Verify `ANT_WrappedLib.dll`, `DSI_CP210xManufacturing_3_1.dll`, and `DSI_SiUSBXp_3_1.dll` are all present **in the same folder as `AntStressMonitor.exe`**.
- Confirm you are running on Windows 10 or 11 (the app is not supported on Windows 7/8).
- Check Windows Event Viewer (`Win + R` → `eventvwr`) → **Windows Logs** → **Application** for a crash entry.

---

## 10. Closing / uninstalling

### Close

Press **Alt + F4** or use Task Manager → End task.

### Uninstall (full removal)

Because the app is portable and writes nothing to the registry:

1. End the process if running (see above).
2. Delete the folder containing `AntStressMonitor.exe` and the three `.dll` files.
3. Delete the Desktop / Start Menu shortcut if you created one.

That's it — nothing else to clean up.
