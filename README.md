# Ant-StressMonitor

A lightweight **portable Windows overlay** that reads your live heart rate from an ANT+ USB dongle and displays a color-coded BPM badge directly on your screen — designed to sit invisibly over the Visual Studio status bar while you code.

---

## Features

| Feature | Detail |
|---|---|
| **Live BPM** | 1-minute rolling average computed from the ANT+ HR broadcast |
| **Colour status** | 🟢 **Green** when avg ≥ 113 BPM (60 % max HR, age 32) · ⚪ **White** otherwise |
| **Time in Zone** | `M:SS` counter that starts when you cross the threshold; auto-resets every 60 minutes |
| **Stealth overlay** | 150 × 20 px frameless transparent bar, always-on-top, no taskbar icon — blends into the VS `#007ACC` status bar |
| **Drag-to-move** | Left-click and drag to reposition anywhere on screen |
| **DPI-aware** | PerMonitorV2 manifest keeps text crisp at any Windows scaling factor |
| **Portable** | Single self-contained `.exe` — no installer, no registry changes |

---

## How it looks

```
┌─────────────────────────────┐  ← VS status bar (#007ACC)
│ 127 BPM              4:32   │  ← overlay (green = in zone)
└─────────────────────────────┘
```

- **`-- BPM` / white** — waiting for first ANT+ reading  
- **`No ANT+` / white** — USB dongle not found  
- **`N BPM` / white** — heart rate below threshold  
- **`N BPM` / green** — heart rate at or above threshold (in zone)

---

## Prerequisites

| Requirement | Notes |
|---|---|
| **Windows 10 or 11** (x86 or x64) | Required — native ANT+ USB DLLs are Windows-only |
| **ANT+ USB stick** | Garmin/Dynastream USB1 or USB2 stick ([example](https://www.thisisant.com/directory/ant-usb-stick-2/)) |
| **ANT+ heart rate monitor** | Any ANT+ certified HR strap or optical sensor |
| **.NET 8 SDK** (build only) | [Download](https://dotnet.microsoft.com/download/dotnet/8.0) — **not needed** if you use a pre-built release |

---

## Quick Start (pre-built release)

> No .NET SDK required.

1. Download the latest `AntStressMonitor-win-x86.zip` from the [Releases](../../releases) page.  
2. Extract the ZIP — you should see:
   ```
   AntStressMonitor.exe
   ANT_WrappedLib.dll
   DSI_CP210xManufacturing_3_1.dll
   DSI_SiUSBXp_3_1.dll
   ```
3. Plug in your ANT+ USB stick.  
4. Put on your ANT+ heart rate monitor.  
5. Double-click `AntStressMonitor.exe`.  

The overlay appears at the top-left of your primary monitor. Drag it onto your VS status bar.

---

## Build from Source

```powershell
# 1. Clone
git clone https://github.com/inolou/Ant-StressMonitor.git
cd Ant-StressMonitor

# 2. Build (debug)
dotnet build AntStressMonitor/AntStressMonitor.csproj

# 3. Publish — single portable exe + native DLLs
dotnet publish AntStressMonitor/AntStressMonitor.csproj -c Release

# Output → AntStressMonitor/bin/Release/net8.0-windows/win-x86/publish/
```

> **Why win-x86?** The ANT+ USB stick driver DLLs (`ANT_WrappedLib.dll` etc.) are 32-bit only. The app runs perfectly on 64-bit Windows in 32-bit mode.

---

## Configuration

All vitality constants are hardcoded in `MainWindow.xaml.cs` — change them and rebuild:

| Constant | Default | Description |
|---|---|---|
| `TargetBpmThreshold` | `113` | BPM at which text turns green. Formula: `(220 − age) × 0.60` |
| `ZoneResetMinutes` | `60` | Minutes before the Time-in-Zone counter resets |

---

## Usage

| Action | How |
|---|---|
| Move the overlay | Left-click and drag |
| Close the overlay | **Alt + F4** or right-click the process in Task Manager → End task |
| Relaunch | Double-click `AntStressMonitor.exe` again |

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `No ANT+` shown | USB dongle not found / driver issue | See [RUNBOOK.md](RUNBOOK.md) — USB driver section |
| `-- BPM` persists | HR monitor not broadcasting | Ensure monitor is on your body and within ~3 m of the dongle |
| Overlay not visible | Window positioned off-screen | Kill the process and relaunch — it opens at top-left |
| App crashes on launch | Missing native DLLs | All three `*.dll` files must be in the same folder as the `.exe` |
| Blurry text | DPI scaling | Right-click the `.exe` → Properties → Compatibility → change DPI settings to "System (Enhanced)" |

---

## Project Structure

```
AntStressMonitor/
├── AntStressMonitor.csproj   # Project + publish config
├── App.xaml / App.xaml.cs    # Application entry point
├── MainWindow.xaml            # 150×20 px overlay XAML
├── MainWindow.xaml.cs         # Vitality logic, UI updates, drag-to-move
├── HeartRateMonitor.cs        # ANT+ radio, device discovery, rolling average
└── app.manifest               # PerMonitorV2 DPI awareness
```

---

## Dependencies

| Package | Version | Purpose |
|---|---|---|
| [SmallEarthTech.AntPlus](https://www.nuget.org/packages/SmallEarthTech.AntPlus) | 6.0.3 | ANT+ device profiles (HeartRate class) |
| [SmallEarthTech.AntUsbStick](https://www.nuget.org/packages/SmallEarthTech.AntUsbStick) | 4.1.0 | USB stick driver wrapper |
| [Microsoft.Extensions.Logging.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions) | 10.0.1 | Null logger (no log output, satisfies DI contract) |

---

## Licence

MIT — see [LICENSE](LICENSE) if present, otherwise use freely.