# TimeWidget

A collection of lightweight always-on desktop widgets for Windows, living in the system tray.
Written in C# / WPF on .NET 9.

## Features

Open any widget from the tray icon (double-click opens the clock):

| Widget | Description |
| --- | --- |
| Clock | Main clock with date and marquee text |
| Files | Quick-access file/folder launcher |
| Media | Now-playing info with progress, seek and album cover |
| Weather | Current conditions and multi-day forecast; China city catalog + alerts |
| Alarm / Timer | Alarms, timers and stopwatch with a ringing window |
| AI Search | Quick AI query box (bring your own API key) |
| Calendar | Month view with per-day events |
| Schedule | Course timetable with week rotation |
| VPN Shortcuts | Launch configured VPN entries |
| Quick Settings | Widget configuration, theme and window snapping |
| Performance | CPU / memory / GPU monitoring |
| Network Traffic | Live up/down throughput |
| Audio Control | Per-application volume sessions and spectrum view |
| Finance | Transactions and purchase plans |

Widgets support drag-to-move, window snapping, and remember their position and
appearance between runs.

## Requirements

- Windows 10 version 1809 (build 17763) or newer
- Windows x64

## Install / Run

The release package is **self-contained** — no .NET runtime installation is needed.

1. Download `TimeWidget-win-x64.zip` from
   [Releases](https://github.com/bismuth083083-stack/TimeWidget/releases) (or the repo root).
2. Extract the zip anywhere (e.g. `C:\Program Files\TimeWidget`).
3. Double-click `TimeWidget.exe`.

The app starts in the system tray. Double-click the tray icon to open the clock widget,
or right-click it to open any other widget. Use **Exit** in the tray menu to quit.

On first launch the app registers itself as a current-user startup item so the widgets
come back after sign-in. To skip that once, set the environment variable
`TIMEWIDGET_SKIP_STARTUP_REGISTRATION=1` before launching.

## Where data is stored

User data lives under `%APPDATA%`, never next to the executable, so the app can be
installed to a read-only location:

- `%APPDATA%\TimeWidget\settings.json` — widget settings and layout
- `%APPDATA%\TimeWidget\weather-cache.json` — weather cache
- `%APPDATA%\TimeWidget\weather-alerts\` — weather alert cache
- `%APPDATA%\DesktopMiniWidgets\` — alarms, calendar events, courses, VPN shortcuts, finance data

## Building from source

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```powershell
# Publish + stage dist/ + create TimeWidget-win-x64.zip
pwsh ./build.ps1

# Same, and copy the single exe to C:\Program Files\TimeWidget
pwsh ./build.ps1 -Install
```

Or with the CLI directly:

```powershell
dotnet publish TimeWidget.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true
```

## Project layout

```
TimeWidget/
  App.xaml(.cs)              application entry + tray icon
  MainWindow.xaml(.cs)       clock widget
  *WidgetWindow.xaml(.cs)    one window per widget
  *Dialog.xaml(.cs)          editor dialogs
  Controls/                  custom WPF controls (spectrum bars, weather icon, marquee)
  Models/                    settings and data records + JSON stores
  Services/                  weather, media, audio, performance, finance, system control
  Assets/                    application icon
  build.ps1                  build & packaging script
```

## License

Personal project. All rights reserved.
