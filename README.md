# AudioSwitch

A portable, persistent **Windows 11 x64 system tray application** for JBL Quantum810 Wireless Game headphones and ASUS Xonar D2 speakers. No main window, console, administrator privileges, installer, external utilities, or separately installed .NET runtime. Launching it does **not** automatically switch your audio output.

## Download and use

In this repository's **Actions → Build Windows x64**, open a successful run and download **AudioSwitch-win-x64** under Artifacts. Extract the ZIP to a permanent folder and launch `AudioSwitch.exe`. Builds are unsigned; Windows may show a SmartScreen warning.

AudioSwitch remains running near the clock. If Windows hides the icon, open the notification area's overflow arrow and drag it into the visible area, or enable it under **Settings → Personalization → Taskbar → Other system tray icons**.

Left-click or right-click its icon to open the same menu:

- 🎧 JBL Quantum810 Wireless Game
- 🔊 ASUS Xonar D2 Audio Device
- Exit

Choose a device to make it the Console and Multimedia default playback endpoint. Communications is unchanged. A checkmark identifies the current Console default; the tray icon shows headphones for JBL, a speaker for ASUS, and a neutral audio symbol for another/no default. If Console and Multimedia differ before selection, the checkmark follows Console. Both roles are verified after selection.

The menu refreshes devices and checkmarks whenever it opens, including changes made in Windows Sound settings. A low-frequency timer refreshes the active icon every 15 seconds while the menu is closed. There is no menu timeout. Selection or clicking outside dismisses it; reopening always works. Switching does not terminate the application or show the previous notification window. Errors use a native tray balloon and detailed log; AudioSwitch remains available to retry. Exit removes the icon and terminates cleanly. Duplicate launches in the same Windows session exit silently. Windows Forms NotifyIcon restores its icon after the Explorer `TaskbarCreated` broadcast.

JBL Game is distinguished from Chat. ASUS matching requires a Speakers endpoint and excludes S/PDIF, digital, Converter, D2X, and other models. Missing, disabled, disconnected, or ambiguous devices have disabled menu entries with status text. Selection re-enumerates devices and rechecks the chosen endpoint ID just before switching. Partial switch failures attempt to restore only Console and Multimedia defaults.

## Start automatically with Windows

1. Keep the extracted EXE in a permanent folder, such as your user folder's `Apps\AudioSwitch`.
2. Press **Win+R**, type `shell:startup`, and press Enter.
3. Create a shortcut to that EXE in the Startup folder (right-click the folder → New → Shortcut).

This starts AudioSwitch at sign-in without administrator rights. Remove the shortcut to disable automatic startup. Moving the EXE requires updating the shortcut. A desktop shortcut is also supported.

## Core Audio property error and diagnostics

`0xE000020B` is SetupAPI's raw **ERROR_NO_SUCH_DEVINST** code; its HRESULT mapping is `0x800F020B`. It indicates that the device instance no longer exists. All-state Core Audio enumeration can include stale removed/unplugged endpoints whose property stores fail even though other active endpoints remain usable. The user's full endpoint-specific log is needed to confirm which device instance produced the reported failure.

The friendly-name PROPERTYKEY was checked against Microsoft's SDK: `{A45C254E-DF1C-4EFD-8020-67D146A850E0}`, PID 14. PROPERTYKEY has explicit 4-byte packing, size 20, and PID offset 16. The x64 PROPVARIANT has size 24, the variant type at offset 0, reserved words at offsets 2/4/6, and its pointer union at offset 8. `IPropertyStore.GetValue` takes a key by reference and returns a variant by reference. Friendly names must be nonempty VT_LPWSTR values. The variant is cleared even if retrieval fails.

Failures are handled per endpoint, recorded with HRESULT and metadata, and represented as unreadable entries. Stale inactive entries do not abort valid active-device discovery. Unreadable active or unknown-state entries disable selection rather than guessing. If an inactive endpoint's name becomes unreadable during the current session, its last known name can be retained by exact endpoint ID to show its disconnected/disabled status; cached names are never used to select unreadable active endpoints. Names are not persisted across application restarts. Unknown inactive names are explicitly reported in menu status/tooltips. Other errors, including access denied, remain distinguishable in the log.

Logs are stored at `%LOCALAPPDATA%\AudioSwitch\AudioSwitch.log`, with one previous log retained after 1 MiB. Logs identify COM initialization and activation, CLSID/IID, endpoint ID/state, PROPERTYKEY, variant type, failing operation, HRESULT, selected output, verification, and rollback. Logging failures do not replace the original audio error. Endpoint names/IDs are local diagnostic data; inspect logs before sharing.

The collection IID regression from the previous implementation is also retained: IMMDeviceCollection uses `0BD7A1BE-7A1A-44DB-8397-CC5392387B5E`. PolicyConfig uses CLSID `870AF99C-171D-4F9E-AF0D-E63DF40C2BC9`, IID `F8679F50-850A-41CF-9C72-430F290290C8`, and SetDefaultEndpoint at zero-based vtable slot 13 including IUnknown. This policy interface is undocumented and may change in future Windows releases.

References: [Microsoft device property keys](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/shared/devpkey.h), [Core Audio interfaces](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/mmdeviceapi.h), [SetupAPI HRESULT](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/shared/winerror.h), and [EarTrumpet policy interface](https://github.com/File-New-Project/EarTrumpet/blob/master/EarTrumpet/Interop/MMDeviceAPI/IPolicyConfig.cs).

## Build and tests

Install the .NET SDK in `global.json`. From the repository root:

```sh
dotnet run --project tests/AudioSwitch.Tests -c Release
dotnet publish src/AudioSwitch/AudioSwitch.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/win-x64
```

The standalone output is `artifacts/win-x64/AudioSwitch.exe`. Original multi-resolution headphones, speaker, and neutral icons are embedded directly in the EXE. Native .NET runtime libraries are also embedded and extracted to the per-user temporary cache on first launch. Linux can cross-compile, but the tray application runs only on Windows.

The dependency-free test runner verifies device matching, exclusions, explicit selection, disconnected/menu/checkmark logic, partial enumeration failures, marshaling layouts, COM identities, vtable order, and HRESULT logging. On Windows, run the additional native smoke test:

```sh
dotnet run --project tests/AudioSwitch.Tests -c Release -- --com-smoke
```

The smoke test performs a Unicode friendly-name PROPERTYKEY/PROPVARIANT round trip through a native Windows memory property store, activates the Core Audio and policy interfaces, enumerates real render endpoints, and reads available endpoint friendly names. Unreadable active/unknown endpoints fail the check; stale inactive metadata is reported. It **never changes audio defaults**. Runners with zero endpoints explicitly report that real endpoint retrieval is unexercised; the native memory-store test still executes.

GitHub Actions runs these checks on `windows-latest`, publishes the Release EXE, uploads it for 30 days, and uploads diagnostics if a check fails.

Actual JBL/ASUS switching and mouse/tray behavior on the user's Windows 11 PC remain unverified. Test both mouse buttons, both target choices, unchanged Communications, checkmarks after Windows Settings changes, unplug/disable/reconnect cases, Explorer restart, duplicate launches, startup, and Exit on that PC before considering hardware behavior verified.
