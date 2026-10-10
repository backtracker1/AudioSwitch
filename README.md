# AudioSwitch

Portable Windows 11 x64 audio output switcher for **JBL Quantum810 Wireless Game** headphones and **ASUS Xonar D2 Audio Device** speakers. Requires no installation, administrator privileges, external tools, or separately installed .NET runtime.

## Usage

Download `AudioSwitch-win-x64` from a successful run under this repository's **Actions → Build Windows x64 → Artifacts**, extract the ZIP, and launch `AudioSwitch.exe`. You can create a desktop shortcut to it. Each launch toggles the default Console playback output: JBL → ASUS, ASUS → JBL, or any other output → JBL. Both Console and Multimedia roles are changed and verified. The Communications role is never changed.

There is no main window, console, tray icon, hotkey, or resident background application. A non-activating notification appears for 2.5 seconds after success, then the application exits. Errors appear for 8 seconds and return exit code 1; click either notification to dismiss it early. Concurrent launches are ignored while the first instance is running.

Device enumeration uses Windows Core Audio endpoint IDs and friendly names. JBL Game is distinguished from Chat; ASUS digital S/PDIF and Converter endpoints are excluded. Only active endpoints can be selected. Missing, disconnected, disabled, or ambiguous target devices cause an error rather than silently selecting another device. If switching fails partway through, the previous Console/Multimedia defaults are restored on a best-effort basis. Communications is untouched even during rollback.

The Console role is the toggle source if Windows has different Console and Multimedia defaults. Device IDs are read fresh each launch, so reconnecting hardware does not depend on stale stored IDs. The app intentionally refuses ambiguous matching devices rather than guessing. Windows or another application can still change the defaults after verification.

## Build

Install the .NET SDK version specified in `global.json`. From the repository root:

```sh
dotnet run --project tests/AudioSwitch.Tests -c Release
dotnet publish src/AudioSwitch/AudioSwitch.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/win-x64
```

The portable output is `artifacts/win-x64/AudioSwitch.exe`. Native runtime components are embedded and extracted by .NET to its per-user temporary cache on first launch. Windows desktop targeting is enabled for cross-compilation on Linux; running the application requires Windows 11 x64. GitHub Actions builds on `windows-latest` for pushes to `main`, pull requests, and manual runs, and uploads the EXE artifact for 30 days. Artifacts require a successful workflow run; adding the workflow alone does not create a downloadable build.

## Validation and limitations

The dependency-free executable test runner validates matching, exclusions, endpoint identity, toggling, unavailable devices, and ambiguous endpoints. It returns a failing exit code if any case fails.

Hardware testing has not been performed. On Windows 11 with both devices connected, verify successive launches select JBL and ASUS; confirm both Console and Multimedia roles, unchanged Communications, automatic notification dismissal, missing/disabled devices, and no remaining AudioSwitch process after dismissal. Windows Core Audio enumeration is documented, but the policy configuration COM interface used to set defaults is undocumented and may change in future Windows versions. The EXE is unsigned; Windows may show SmartScreen warnings for a downloaded build.
