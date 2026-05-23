# MiniStatTray

[简体中文](README.zh-CN.md) | English

MiniStatTray is a tiny Windows notification-area monitor for CPU load, GPU power, memory usage, and live network throughput.

It is designed to stay lightweight and avoid low-level sensor drivers. NVIDIA GPU power is read through NVML when available; CPU, memory, and network stats use Windows APIs.

## What It Shows

Five tray icons are shown in a fixed order:

1. CPU usage
2. GPU power in watts
3. Memory usage
4. Download speed
5. Upload speed

Hover an icon to see the full metric name and value.

## Download

The ready-to-run executable is in:

`dist/MiniStatTray.exe`

Double-click it to run. No installer is required.

## Runtime Requirement

MiniStatTray is built with .NET Framework because it can use Windows tray/WinForms APIs directly and keeps the executable very small.

Most Windows 10 and Windows 11 PCs already include a compatible .NET Framework 4.x runtime. If the app does not start and Windows asks for .NET Framework, install the official Microsoft runtime:

- [.NET Framework 4.8 Runtime](https://dotnet.microsoft.com/download/dotnet-framework/net48)
- [.NET Framework 4.8.1 Runtime](https://dotnet.microsoft.com/download/dotnet-framework/net481)

For maximum compatibility, .NET Framework 4.8 is the safest recommendation for older supported Windows versions.

## Build

On Windows with .NET Framework installed:

```powershell
.\build.ps1
```

The compiled executable will be written to:

`dist/MiniStatTray.exe`

## Notes

- The app is intentionally small and uses only built-in Windows/.NET Framework APIs plus NVIDIA NVML if present.
- It does not install drivers.
- It does not require administrator privileges for normal use.
- Windows may hide new notification icons under the tray overflow menu; pin them manually if desired.
