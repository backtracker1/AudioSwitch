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

## COM diagnostics and regression fix

The initial implementation declared the wrong `IMMDeviceCollection` IID. The Windows SDK specifies `0BD7A1BE-7A1A-44DB-8397-CC5392387B5E`; the incorrect IID caused interface marshalling during `EnumAudioEndpoints` to fail before device selection or PolicyConfig switching.

Enumerator activation requests CLSID `BCDE0395-E52F-467C-8E3D-C4579291692E` with IID `A95664D2-9614-4F35-A746-DE8DB63617E6`. Policy activation requests CLSID `870AF99C-171D-4F9E-AF0D-E63DF40C2BC9` with IID `F8679F50-850A-41CF-9C72-430F290290C8` (the Win7/Win8 and Windows 10 RS1 onward variant also used by EarTrumpet). `SetDefaultEndpoint` occupies zero-based vtable slot 13 including IUnknown, returns HRESULT, and takes an LPCWSTR endpoint ID and a 32-bit ERole. The ten preceding undocumented slots are reserved and never called; their unrelated version-dependent signatures are intentionally not exposed.

Sources checked: [Microsoft Windows SDK mmdeviceapi.h](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/mmdeviceapi.h), [EarTrumpet policy interface](https://github.com/File-New-Project/EarTrumpet/blob/master/EarTrumpet/Interop/MMDeviceAPI/IPolicyConfig.cs), and [policy coclass](https://github.com/File-New-Project/EarTrumpet/blob/master/EarTrumpet/Interop/MMDeviceAPI/PolicyConfigClient.cs). This verifies declarations against references; it does not establish hardware compatibility.

Diagnostics are written to `%LOCALAPPDATA%\AudioSwitch\AudioSwitch.log`, with one previous file retained when the log exceeds 1 MiB. Logs identify activation CLSID/IID, native HRESULTs, exact COM operations, exceptions, selected endpoints, rollback attempts, and successful default verification. Error notifications include the log location. Logging failures do not replace the original error. Logs contain local endpoint names and IDs; inspect them before sharing.

The test runner includes COM identity/vtable regressions and verifies operation/HRESULT logging. On Windows, additionally run:

```sh
dotnet run --project tests/AudioSwitch.Tests -c Release -- --com-smoke
```

This activates both COM interfaces and enumerates devices without changing any defaults. GitHub Actions runs this on its Windows runner and uploads diagnostic logs if a step fails. A CI activation smoke test does not test actual switching with JBL/ASUS hardware.
