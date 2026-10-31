# 0025 — Windows MSIX: built by the app's own project, signed afterwards, test-signed until there is a certificate

Status: accepted. Extends decision 0014 (the app stays unpackaged by default).

**Decision.**

- The project builds two ways. The default is unchanged: unpackaged and self-contained, a folder or zip (`windows.yml`, artifact `Clipo-win-x64`). With `-p:WindowsPackageType=MSIX` the same project builds an MSIX from `windows/Clipo/Package.appxmanifest` and `windows/Clipo/Assets/` (logos from `assets/make-windows-icon.py --logos`). The Windows App SDK stays bundled (self-contained); the package declares no framework dependency.
- `windows/scripts/make-msix.ps1 <output folder>` runs that build unsigned, signs the `.msix` with `signtool`, checks the signer and writes `Clipo-<version>-x64.msix` with a `.sha256` file. `-Platform ARM64` builds for ARM64, if the core was packaged for it.
- Signing is chosen by two environment variables:
  - `CLIPO_SIGN_PFX` and `CLIPO_SIGN_PFX_PASSWORD` name a code-signing certificate. The script stops unless its subject equals `Publisher` in the manifest. The signature is timestamped (`CLIPO_TIMESTAMP_URL`, default DigiCert), so it outlives the certificate.
  - Without them the script creates a throwaway self-signed certificate for the manifest's `Publisher` (`CN=Clipo`), signs with it, removes it from the certificate store and writes the public part as `Clipo-test-signing.cer`. The file is `Clipo-<version>-x64-selfsigned.msix`.
- `.github/workflows/windows-msix.yml` runs the script on the hosted `windows-latest` runner for a `v*` tag, a manual run, and pull requests that touch the app or the packaging. It uploads the `Clipo-windows-msix` artifact. For a tag it fails unless the tag matches the manifest's version. The PFX comes from the repository secrets `CLIPO_SIGN_PFX_BASE64` and `CLIPO_SIGN_PFX_PASSWORD`, which pull requests never get.
- The version lives in `Package.appxmanifest` (four numbers, the last 0); a test checks its shape, the startup task id, the languages and the logos against the code and the files.
- **Start at sign-in.** A packaged app's registry writes are private to the package, so the `Run` key of decision 0017 does nothing there. The package declares a startup task (`ClipoStartup`, off until chosen) and the packaged app uses it: `PackagedLoginItem` behind the new `ILoginItem` interface, picked at run time by `PackageIdentity.IsPackaged`. Windows keeps its state, so a switch-off in Settings > Apps > Startup shows the same note as before, and there is nothing to repair. The task cannot pass `--background`, so a copy started by it is recognised by its activation kind.

**Why.**

- Letting the project build its own package means the Windows App SDK's tooling registers the WinUI classes and builds the resource index. Packing the unpackaged output by hand would have to guess both, and no one can check that without a Windows PC.
- Signing afterwards with `signtool` keeps the build the same whatever signs it. A cloud signing service (Azure Trusted Signing) later only changes the sign step.
- An MSIX does not install unless it is signed by a certificate the PC trusts, so unsigned is not an option. A throwaway certificate gives something that can be installed and tested now; the real one is a setup task, as the Apple account is for the dmg (decision 0023). No signing key is ever in the repository, and the test key never leaves the runner.
- The repository stays free to build: nothing here needs a paid account until release.

**Installing a test-signed package.** Download the artifact, then in an administrator PowerShell: `Import-Certificate -FilePath .\Clipo-test-signing.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople`, and `Add-AppxPackage .\Clipo-<version>-x64-selfsigned.msix` (or double-click the package). Every build has a new certificate, so import the new one each time; remove old ones in `certlm.msc` > Trusted People when done.

**Setting up a real certificate (once there is one).**

1. Set `Publisher` in `Package.appxmanifest` to the certificate's subject, exactly as `certutil -dump` shows it.
2. Add the repository secrets `CLIPO_SIGN_PFX_BASE64` (the `.pfx`, base64) and `CLIPO_SIGN_PFX_PASSWORD`.
3. Push a tag such as `v0.1.0`, or run the workflow by hand.

**Consequences.**

- A packaged Clipo has its own data. Windows redirects the app's writes under `%LOCALAPPDATA%` into the package (`%LOCALAPPDATA%\Packages\<package>\LocalCache\Local\Clipo`), so a packaged install starts with an empty history and settings, not those of the zip build, and uninstalling removes them. The Credential Manager key does not move; it is the same target for both.
- Only x64 is built in CI; ARM64 PCs run it under emulation. ARM64 needs the core packaged for it first (`cargo xtask package windows`) and the same script with `-Platform ARM64`.
- Until a certificate exists, the package works only on PCs that trust the test certificate. SmartScreen and the Microsoft Store are later steps.
- Not yet run on a Windows PC, like the rest of the Windows app. The first things to check there: the package installs and starts, the tray icon and shortcut work, the language switch and the restart from Settings work under package identity, Start at sign-in appears in Settings > Apps > Startup and starts Clipo without a window, and an update over an older version keeps the history.
- The build commands were written without a Windows machine; the first CI run of `windows-msix.yml` is the first time they run.
