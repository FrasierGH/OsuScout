<img src="branding/scoutsu_256.png" width="96" alt="Scoutsu icon">

# Scoutsu

Scoutsu (formerly OsuScout) is a desktop tool for osu! players, on Windows and Linux, that tags every map in your library by playstyle (streams, jumps, tech...) with machine learning, so you can find the kind of map you want to play.

## Features
- Machine learning integration via ONNX for beatmap classification and feature extraction
- Runs on Windows and Linux
- Automatic background updates via GitHub Releases
- Works with both osu!stable and osu!lazer

## osu!stable and osu!lazer
Scoutsu finds, tags and searches the maps of either client. On first launch it picks whichever one is installed, or asks if you have both. The **Library** picker at the top switches between them, and each client keeps its own library, so switching never mixes them.

- **Finding your maps.** For stable, Scoutsu reads your `Songs` folder. For lazer, it reads lazer's data folder (`%APPDATA%\osu` on Windows, `~/.local/share/osu` on Linux, or wherever you moved it in lazer's settings). Scoutsu only ever reads from it and never changes your lazer install. If either isn't found, pick it with **Folder…**.
- **New maps** show up while Scoutsu is open, on both clients.
- **Using it with the game.** Scoutsu is a normal window: Alt+Tab out of osu!, pick a map and click **Copy & switch to osu!**. That puts you back in the game (restoring it if fullscreen minimised it) with the search on your clipboard; press Ctrl+V in song select.
- **On Linux** switching to the game works on X11 when `xdotool` is installed. Wayland doesn't let one app bring another to the front, so there Scoutsu copies the search and you switch to osu! yourself.
- **What gets copied.** On lazer, the beatmap ID, which song select matches to that exact map. On stable, and for maps with no online ID, artist, title and difficulty.

## Installation
Download from the [Releases](https://github.com/FrasierGH/OsuScout/releases) page.

**Windows:** run `Scoutsu-Setup.exe`. It installs, launches, and adds desktop and Start menu shortcuts.

**Linux:** download `Scoutsu-linux-x64.AppImage`, make it executable and run it:

```bash
chmod +x Scoutsu-linux-x64.AppImage
./Scoutsu-linux-x64.AppImage
```

AppImages need FUSE; if it won't start, install your distribution's `libfuse2` package (or run it with `--appimage-extract-and-run`).

Both update themselves from this page. This fork (osu!stable and osu!lazer) installs as its own app, next to the original [Scoutsu](https://github.com/AliKhairy/OsuScout) if you have it.

To build it yourself instead: `dotnet publish OsuScoutNew/OsuScoutNew.csproj -c Release -r win-x64 --self-contained true -o publish` (or `-r linux-x64`), then run `OsuScoutNew` from the `publish` folder.

## License
Copyright (C) 2026 AliKhairy

Scoutsu is free software, licensed under the [GNU General Public License v3.0](LICENSE). You can use, study, change and share it; if you distribute a modified version, it has to stay under the same license with its source available.

The original Scoutsu was GPL because it bundled [OsuMemoryDataProvider](https://github.com/Piotrekol/ProcessMemoryDataFinder) (GPL-3.0-or-later) to hide its window while you played. This fork no longer uses it, but as a modified version of Scoutsu it stays under GPL-3.0.

Other libraries in the installer: [Avalonia](https://github.com/AvaloniaUI/Avalonia) (MIT), [ONNX Runtime](https://github.com/microsoft/onnxruntime) (MIT), [Entity Framework Core](https://github.com/dotnet/efcore) (MIT), [Velopack](https://github.com/velopack/velopack) (MIT), [SQLitePCL.raw](https://github.com/ericsink/SQLitePCL.raw) (Apache-2.0) and Rosu.Net, a wrapper around [rosu-pp](https://github.com/MaxOhn/rosu-pp) (MIT) for star ratings.

The tagging model was trained on community tags from [echosu](https://echosu.com).
