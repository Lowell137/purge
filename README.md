# Purge

A Windows 11 uninstaller built with WinUI 3. It removes programs silently, cleans up what they leave behind, and bundles a set of related tools.

## Features

**Programs**
- Lists desktop programs and Store apps with icons, size and install date; search, filter and sort.
- Silent uninstall with no uninstaller windows or console popups. The silent switch is derived from the installer type: MSI, Inno Setup, NSIS, Squirrel, WiX Burn, winget, Chromium.
- Programs without a silent mode are never opened by surprise: Purge asks whether to force-remove, show the uninstaller, or skip.
- Leftover scan after uninstall: folders, files, registry keys and values, services, scheduled tasks, Run entries, App Paths, SharedDLLs, Installer folders, MuiCache, firewall rules, CLSIDs and Tracing keys.
- Files go to the Recycle Bin and registry keys are exported to `.reg` before deletion.
- Force uninstall, batch uninstall, optional restore point, uninstall history.
- Drop an `.exe`, shortcut or folder on the window to uninstall the program it belongs to.

**Startup** – turn startup entries on and off (same mechanism as Settings › Startup apps).

**Tools**
- Install Monitor: snapshot before and after an install, then remove everything it added.
- Update outdated software through winget, with select all.
- Easy Uninstall: pick an open window from a list or click one on screen; a global hotkey works on the focused window.
- Force Uninstall+: remove by file or folder.
- Stubborn Program Remover: program folders with no uninstall entry.
- File Shredder, Software Relocator (move a program to another drive with a junction), Bloatware Remover, Windows Updates Uninstall.

**Settings** – app theme, window material (Mica, Mica Alt, Acrylic, Solid), uninstall defaults, hotkey toggle, data folders.

The layout adapts to the window size: the navigation pane, list columns, toolbars and tool cards reflow down to about 480 px wide.

## Install

Download `PurgeSetup-<version>-x64.exe` from [Releases](../../releases). The installer is per-user by default (no admin prompt) and can install for all users. Windows 10 2004 (19041) or later, x64.

Some actions need administrator rights (uninstalling machine-wide programs, restore points, Windows updates); use **Restart as administrator** in the app.

## Build

Requires the .NET 8 SDK.

```powershell
dotnet build -p:Platform=x64
# release + installer (Inno Setup 6)
dotnet publish -c Release -p:Platform=x64 -r win-x64 --self-contained true -o publish
ISCC.exe installer\Purge.iss
```

The app icon is drawn from vectors by `tools\make-icon.ps1`.

## Data

Settings, history, monitor logs and registry backups live in `%LOCALAPPDATA%\Kaldir`.
