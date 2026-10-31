# 0014 — Windows app: WinUI 3 unpackaged, core as a class library, Credential Manager key

Status: accepted

**Decision.**

- `windows/Clipo.slnx` holds four .NET 10 projects:

  | Project | What it is |
  |---|---|
  | `Clipo` | The WinUI 3 app (Windows App SDK 2.6), `net10.0-windows10.0.26100.0`, minimum Windows 11 (10.0.22000) |
  | `Clipo.Core` | Only the generated bindings (`core/dist/windows/ClipoCore.cs`, namespace `Clipo.Core`), with analyzers off |
  | `Clipo.Services` | App logic that needs no WinUI: opening the history, the Credential Manager key store |
  | `Clipo.Tests` | xUnit tests for `Clipo.Services` and the string tables, against the real core library |

- The app is unpackaged (`WindowsPackageType=None`) and bundles the Windows App SDK (`WindowsAppSDKSelfContained`). MSIX packaging is step 5.3.
- `cargo xtask package windows` writes the core package (decision 0008). Run it before building. Projects that load the core (the app and the tests) copy `clipo_ffi.dll` for their architecture next to their output (`windows/Directory.Build.targets`).
- The history is `%LOCALAPPDATA%\Clipo\history.db`. Its key is a generic credential in the Windows Credential Manager, target `io.github.arash7l3.Clipo/history-key`, persisted for this user on this machine only (`CRED_PERSIST_LOCAL_MACHINE`).
- Strings are `.resw` files in `Clipo/Strings/en-US` and `Clipo/Strings/fa`, read through MRT Core (`x:Uid` and `ResourceLoader`).
- One copy per sign-in session, through a named mutex. A second launch exits.
- Until the tray icon (step 3.2), the app shows a small status window: the history's state and Try Again.
- Package versions are pinned centrally in `windows/Directory.Packages.props`. Every project except `Clipo.Core` builds with nullable reference types, the recommended .NET analyzers and warnings as errors.
- CI: `.github/workflows/windows.yml` runs on GitHub's hosted `windows-latest`, free while the repository is public. It runs the C# smoke test and the tests, and publishes the app for x64 as a downloadable artifact (`Clipo-win-x64`).

**Why.**

- Unpackaged runs straight from the build output and from a zip, which makes testing on a Windows PC easy before installers exist. Bundling the Windows App SDK means the PC needs nothing installed.
- Keeping logic out of the WinUI project lets the tests run on any OS: `Clipo.Services` and `Clipo.Tests` are plain `net10.0`, so they also build and pass on macOS and Linux against `libclipo_ffi`. Only the XAML compiler needs Windows.
- A separate bindings project lets the generated file skip the analyzers without loosening them for the app's own code.
- Local app data does not roam, and the clipboard history must stay on the machine. The Credential Manager encrypts credentials with the user's logon secret, the Windows counterpart of the login keychain. Roaming persistence (`CRED_PERSIST_ENTERPRISE`) would copy the key to other machines.

**Safeguards.**

- As on macOS (decision 0009): a missing or wrong key never deletes anything by itself. The app asks, and only then deletes the database and the key. A Credential Manager failure other than "not found" is reported as unavailable with Try Again, not as a missing key.
- Log lines are error types and core messages, which carry codes and ids, never clipboard content.

**Consequences.**

- The app needs Windows to compile, because the XAML compiler is a Windows program. On macOS and Linux, build and run the tests with `-p:ClipoBindings=` and `-p:ClipoNativeLibrary=` pointing at a local build (see the README).
- CI checks that the app compiles and the tests pass, but nobody has run it yet. A manual run on Windows 11 is needed before the Windows phase can be called done.
- If the repository goes private again, the Windows job uses paid Actions minutes, or needs a self-hosted Windows runner.
- An unsigned, unpackaged app may get a SmartScreen warning when started from a downloaded zip.
