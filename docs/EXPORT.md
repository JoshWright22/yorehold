# Exports

`export.ps1` makes each build. It never opens a window or waits for a key. Builds go to
`../.dev/export/<platform>/`, logs to `../.dev/godot-export-*.log`. `ALL OK` and exit code 0 means good.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File export.ps1 windows [-Shot name.png] [-Screen title] [-Frames N]
powershell -NoProfile -ExecutionPolicy Bypass -File export.ps1 android
powershell -NoProfile -ExecutionPolicy Bypass -File export.ps1 ios
```

Add `-DebugBuild` for a debug build. The presets are in `export_presets.cfg` (Windows Desktop, Android, iOS).

## What every export needs

- The official export templates for this exact engine, `4.6.1.stable.mono`, in
  `%APPDATA%\Godot\export_templates\4.6.1.stable.mono\`. They are the
  `Godot_v4.6.1-stable_mono_export_templates.tpz` from the 4.6.1-stable release on GitHub (1.2 GB); the
  `.tpz` is a zip with a `templates/` folder. Only `version.txt`, `icudt_godot.dat`, the `windows_*x86_64*`
  files, the `android_*` files and `ios.zip` are needed, which is what this machine has. The editor's
  Manage Export Templates does the same in one go.
- The dotnet SDK (8 for desktop; 9 or later for Android, see below). The export runs `dotnet publish` itself.

## How content gets into a build

The rules read content with System.IO, which can't open files inside a `.pck` or an APK. So an exported
game copies `res://assets` out to `user://content` each time it starts (`src/PackedContent.cs`, a few hundred
KB) and reads that. The run prints `Unpacked N content files to ...`. The presets add `assets/*.json` and
`assets/*.md` to the pack and leave `rules/`, `tests/` and `docs/` out (the rules are in the game's assembly
already). The PNGs in `assets/ui` are packed as imported textures only; nothing reads them as files.

In an exported game the screenshot run's folders (`.dev/shot-*`) and relative `shot` paths are under the user
folder instead of the workspace, since there is no project folder.

## Windows

Works. `export.ps1 windows` exports `Yorehold.exe`, `Yorehold.console.exe` (prints the log; use it from a
terminal), `Yorehold.pck` and `data_Yorehold_windows_x86_64/` (the .NET runtime and assemblies). All four go
together. It then starts the console exe headless for 30 frames into the play screen and fails on any error
or if the content wasn't unpacked. `-Shot name.png` adds a screenshot run of the exported exe, the same as
`check.ps1 -Shot` but without a script.

The exe has no icon of its own yet: Windows icons need an `.ico` (`application/icon` in the preset).

## Android

Builds a signed release APK (arm64 only, about 100 MB). Not run on a device yet: none was attached and the
SDK on this machine has no emulator.

What it takes:

- Android SDK with `platform-tools` and `build-tools` (this machine: `%LOCALAPPDATA%\Android\Sdk`, build-tools
  36.1.0) and a JDK. Both paths are in the Godot editor settings (`export/android/android_sdk_path`,
  `export/android/java_sdk_path`, here JDK 24). Godot's docs ask for JDK 17; 24 signs fine without Gradle.
- `rendering/textures/vram_compression/import_etc2_astc=true` in `project.godot`. Without it the export
  fails with only the "C# is experimental" line and no reason.
- The game project targets `net9.0` when building for Android (`Yorehold.csproj`), since Godot 4.6's
  prebuilt Android templates only take .NET 9. The rules project stays on `net8.0`.
- No Gradle build: the prebuilt template is used.
- A project icon (`icon.svg`, a placeholder: an amber Y on ink).

Signing: the release keystore is `export/keystore/yorehold-release.keystore` with its password in
`export/keystore/password.txt`. git ignores `export/`. `export.ps1` makes both the first time and passes them
to Godot through `GODOT_ANDROID_KEYSTORE_RELEASE_*`, so no password sits in `export_presets.cfg`. Keep a
copy of that folder somewhere safe: an app signed with a lost key can't be updated on Google Play. Debug
builds use the editor's debug keystore.

To try it on a phone: turn on USB debugging, then
`%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe install -r ..\.dev\export\android\Yorehold.apk`.

## iOS

Can't be done on this machine. Godot refuses it before building anything: "Exporting to an Apple Embedded
platform when using C#/.NET is experimental and requires macOS." C# on iOS is compiled ahead of time with
NativeAOT, which needs Xcode.

What it would take, on a Mac with Xcode and the dotnet SDK:

- the same templates (`ios.zip` is installed here already, but a Mac needs its own copy),
- an Apple developer team ID in the preset (`application/app_store_team_id`),
- `export.ps1 ios` or the editor's export, which with `application/export_project_only` on writes an Xcode
  project to build and sign there.

Unknown until then: whether everything survives NativeAOT trimming. JSON is read from `JsonElement` and
written as `JsonNode` by hand, with none of our types serialised by reflection, which is the usual thing
that breaks.
