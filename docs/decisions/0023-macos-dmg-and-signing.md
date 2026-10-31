# 0023 — macOS dmg: one script, Developer ID when available, ad-hoc until then

Status: accepted. Replaces the "5.2 is blocked" part of decision 0006.

**Decision.**

- `macos/scripts/make-dmg.sh` builds the Release app (arm64 + x86_64), signs it with the hardened runtime and no entitlements, and puts it in `Clipo-<version>.dmg` next to an `Applications` link, with a `.sha256` file. The version is `MARKETING_VERSION`.
- Signing is chosen by two environment variables:
  - `CLIPO_SIGN_IDENTITY` names a "Developer ID Application" identity in the keychain. Without it the app gets an ad-hoc signature and the file is `Clipo-<version>-unsigned.dmg`.
  - `CLIPO_NOTARY_PROFILE` names a `notarytool` keychain profile. With it the app is notarized and stapled, then the dmg is signed, notarized and stapled, and both are checked with `spctl`.
- `.github/workflows/macos-dmg.yml` runs the script on the self-hosted Mac for a `v*` tag or a manual run, and uploads the dmg as the `Clipo-macos-dmg` artifact. For a tag it fails unless the tag matches the app's version. Pull requests that change the script or the workflow build an ad-hoc dmg to check it, and upload nothing.
- The workflow reads the two names from repository variables (`CLIPO_SIGN_IDENTITY`, `CLIPO_NOTARY_PROFILE`). The certificate, its private key and the notary password stay in the Mac's login keychain; no signing secret is stored in GitHub. Pull requests never get the names, so code from a pull request is never signed with the Developer ID.

**Why.**

- The repository is public and runs on a self-hosted Mac. Keeping the key in that Mac's keychain avoids copying a `.p12` into GitHub secrets and importing it on every run.
- Notarizing the app on its own, not only the dmg, staples a ticket to the app, so it opens offline after it is copied out of the dmg.
- The ad-hoc path runs the same steps, so the script is tested now and only the two variables change when the account exists.

**Setting up signing (once there is an Apple Developer account).**

1. Create a "Developer ID Application" certificate and install it in the login keychain of the runner's Mac user.
2. Let `codesign` use the key without a prompt: `security set-key-partition-list -S apple-tool:,apple:,codesign: -s ~/Library/Keychains/login.keychain-db` (asks for the login password).
3. Store the notary credentials: `xcrun notarytool store-credentials clipo-notary --apple-id <id> --team-id <team>` with an app-specific password, or `--key`, `--key-id` and `--issuer` for an App Store Connect API key.
4. Set the repository variables `CLIPO_SIGN_IDENTITY` to the identity's full name and `CLIPO_NOTARY_PROFILE` to `clipo-notary`.
5. Push a tag such as `v0.1.0`, or run the workflow by hand.

**Consequences.**

- Until then the dmg is ad-hoc signed. On another Mac, Gatekeeper blocks the first launch; the user opens it from System Settings > Privacy & Security > Open Anyway, or runs `xattr -dr com.apple.quarantine /Applications/Clipo.app`.
- An ad-hoc signature changes with every build. After each update macOS asks again before giving Clipo its key from the keychain, and Accessibility (for auto-paste) has to be granted again. A Developer ID signature keeps both across updates.
- Moving the key to the data protection keychain (decision 0009) is not done yet. It needs a keychain access group entitlement, which on macOS needs a provisioning profile from the developer account; an ad-hoc app with that entitlement does not launch. It moves to a later step, once the account exists, with a one-time key migration that copies the key, reads it back and only then deletes the login keychain item.
- The app has no icon yet, so Finder shows the generic app icon in the dmg and in Applications.
