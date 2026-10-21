# AGENTS.md — G-Helper

Lightweight Armoury Crate alternative for Asus laptops/handhelds. WinForms, `net10.0-windows`, x64-only, single self-contained exe.

## Build

```
dotnet build app/GHelper.sln                 # from repo root
dotnet publish app/GHelper.sln -c Release --runtime win-x64 -p:PublishSingleFile=true --no-self-contained
```

- Requires the .NET 10 SDK. Solution is `app/GHelper.sln` (single project `app/GHelper.csproj`).
- There is **no test project**. Verify changes by building and running the exe.
- A `KillRunningGHelper` MSBuild target kills any running instance before each local build (skipped on CI). Expected, not a bug.
- Publish output is fixed at `app/bin/x64/Release/net10.0-windows/win-x64/publish/GHelper.exe`; release tags must match `AssemblyVersion` in the csproj — bump both together. See `.github/workflows/build.yml`, `.github/workflows/release.yml`.

## Architecture

- Boot order matters: `app/Program.cs` constructs controls on static `Program.*` fields (`settingsForm`, `modeControl`, `gpuControl`, `inputDispatcher`, `acpi`, …). Controls are plain classes with no common base — they read `Program.settingsForm` / `Program.modeControl` directly. Only real interfaces: `app/Gpu/IGpuControl.cs` and `app/Peripherals/IPeripheral.cs`. All forms derive from `app/UI/RForm.cs`.
- `app/AppConfig.cs` is the feature-flag mechanism: JSON config in `%APPDATA%\GHelper\config.json`, plus ~100 model predicates (`IsAlly()`, `IsTUF()`, `IsSlash()`, `Is(str)`, `Get/GetString`). New per-device quirks normally become a new predicate here plus a config key. Config writes are lazy (2s) and atomic — don't hand-edit while running.
- `app/AsusACPI.cs` talks to `\\.\\ATKACPI` (hotkey scan codes, `AsusMode`/`AsusFan`/`AsusGPU`). `app/HardwareControl.cs` is the static sensor cache.
- All logging goes through `Helpers.Logger.WriteLine`. HID/connect/sync loops must never block the UI thread — run them under `Task.Run`.

## Localization

Add new strings **only** to the neutral `app/Properties/Strings.resx`; reference them as `Properties.Strings.Xxx`. Translated `Strings.<lang>.resx` files are synced by Crowdin (`crowdin.yml`) — don't hand-edit them. `docs/README*.md` are marketing/docs-site pages, unrelated to the resx system.

## Adding keyboard support (ROG Strix Scope II 96 Wireless / Omni)

Keyboards are hardcoded model classes, not config-driven.

1. Model classes live in `app/Peripherals/Keyboard/Models/` — one file per family, e.g. `StrixScopeII.cs` holds `StrixScopeII`, `StrixScopeIIRX`, `StrixScopeII96Wireless`, `StrixScopeII96RXWireless`, `StrixScopeII96WirelessOmni`, `StrixScopeII96RXWirelessOmni`.
2. Each class extends `AsusKeyboard` with a ctor of `(vendorId 0x0B05, productId, pathFragment, reportId)`. Current Scope II 96 PIDs: wireless `0x1AAE`, RX `0x1B78`, OMNI `0x1ACE` with path `"mi_02&col02"` and reportId `0x02`. RX/OMNI variants reuse the `protected` ctor that takes a PID rather than duplicating overrides.
3. Override only what differs: `GetDisplayName()`, `LayoutName()` (ISO vs ANSI via `IsIsoLayout`), `HasBattery()`, `UsesChunkedWaveEffects()`, `USBPacketSize()`.
4. Register the model with one `DetectKeyboard(new Xxx());` line inside `DetectAllAsusKeyboards()` in `app/Peripherals/PeripheralsProvider.cs`. Mice and headsets have their own separate lists.
5. Layouts: `LayoutName()` must match a key in `AuraKeyboardLayouts.Data` (`app/Peripherals/Keyboard/AuraKeyboardLayouts.cs`) — rows plus the LED-id map. Missing keys silently fall back to `"StrixScopeII"`, so a wrong name shows the wrong per-key lighting grid rather than an error. Note `StrixScopeII96RxRows` derives from `StrixScopeII96Rows` and only replaces the bottom row.

### OMNI receiver pitfalls

- The OMNI dongle (`OMNI_PID = 0x1ACE`) is one receiver exposing several HID collections: `mi_02&col01` (discovery hub), `mi_02&col02` (keyboard, vendor usage `0xFF00`, reportId `0x02`), `mi_02&col03` (mouse, reportId `0x03`). Match instances across collections with `OmniInstance(devicePath)` — never assume a single path.
- Keyboard traffic must go to `col02`; `DedectOmniMouse()` otherwise uses `col03`. `PeripheralEventListener` skips the `mi_00` pointer collection on purpose.
- In `Connect(AsusKeyboard)`, an OMNI-PID keyboard that is still not ready after 3 sync retries is disposed and skipped. Don't remove that guard — it prevents a phantom keyboard entry when the dongle is asleep.
- OMNI reports are 64 bytes (`USBPacketSize() == 64`). Direct per-key writes pack 14 keys max per packet; larger batches drop the last blue byte.
- `AsusKeyboard.FindVendorDevices()` prefers usage page `0xFF00` interfaces with ≥64-byte output reports and falls back to the rest.

### Other peripheral rules

- Per-device config keys derive from the concrete type name: `ConfigKey(name) => $"kb_{GetType().Name}_p{Profile}_{name}"`, plus `keyboard_profile_{TypeName}` and `bind_{XXXX}` key remaps. Renaming a model class silently loses that device's saved config.
- Settings UI: `AsusKeyboardSettings` / `AsusMouseSettings` / `AsusHeadsetSettings` are instantiated by `Settings.cs` on the `DeviceChanged` event, one instance per type.
- To preview a layout without hardware, set the `keyboard_test` config key (hex/dec PID) — see the `TestMode` handling at the top of `DetectAllAsusKeyboards()`.
- `0x5E` is *not* a keyboard report id — it's the Anime Matrix / Slash / XGM report id (`app/AnimeMatrix/SlashDevice.cs`, `app/USB/XGM.cs`).

## Related docs

`.github/CONTRIBUTING.md` · `.github/SECURITY.md` · `docs/README.md` (feature overview) · issue forms under `.github/ISSUE_TEMPLATE/`.
