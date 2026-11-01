# 0029 — A stable self-signed signature, a build number per release, and snippets in the history

Status: accepted

**Decision.**
- **Signing without a Developer ID:** `macos/scripts/self-signing.sh` keeps one self-signed code-signing certificate ("Clipo Self-Signed") on the build Mac, in its own keychain (`~/Library/Keychains/clipo-self-signing.keychain-db`, random password in `~/Library/Application Support/Clipo Build/`). It is made on first use and added to the keychain search list. `make-dmg.sh` signs with it when `CLIPO_SIGN_IDENTITY` is unset (`CLIPO_SELF_SIGN=0` gives the old ad-hoc signature) and fails unless the designated requirement names the certificate. The dmg is `Clipo-<version>-selfsigned.dmg`. A Developer ID still replaces it (decision 0023).
- **Build number:** `CURRENT_PROJECT_VERSION` is `$(MARKETING_VERSION)`, so `CFBundleVersion` changes with each release; `make-dmg.sh` checks that they match and gives the bundle a fresh date.
- **Icon inside the app:** at launch Clipo sets `NSApp.applicationIconImage` from `AppIcon.icns`, so its alerts and About panel never depend on the system's icon cache.
- **Snippets in the history:** a snippet picked from the menu-bar or tray menu is written to the clipboard without telling the watcher to skip it, so the history records it like any copy (subject to private mode and the ignore list) and it shows at the top. The snippet itself is unchanged; deleting history does not delete snippets. On Windows the history credits it to Clipo, on macOS to the app in front.

**Why.** A check on the build Mac (macOS 27) after 0.1.3 showed:
- every build was ad-hoc signed, so its designated requirement was the build's own hash (`cdhash H"…"`). macOS ties the Accessibility permission to it, so each update silently lost auto-paste, and the menu's snippet stopped pasting in 0.1.3;
- every release had build number 1, and the bundle kept the date of the first build. Asked by path, macOS drew the right icon, but lookups by bundle id (Spotlight, the Accessibility list, the app's own alerts) kept the blank icon cached for that id and build number from a build before the icon existed;
- users expect a pasted snippet to show up in the clipboard history, and both apps kept it out.

**Consequences.**
- Moving from the ad-hoc to the self-signed signature changes the requirement once: after installing 0.1.4, remove Clipo from Privacy & Security > Accessibility with "−" and turn it on again, and allow the keychain prompt once. Later updates keep both.
- The self-signed certificate is not trusted by Gatekeeper, so the first launch still goes through Privacy & Security > Open Anyway, as before.
- Anyone who can run code as the build Mac's user can sign with the certificate. That user can already change the app being built, so this adds no new access; forks never run on that Mac.
- Decisions 0019 and 0020 said a snippet "is not history and moves nothing to the top"; that part is replaced.

**Update.** Snippets stay out of the history again, at the user's request: picking one tells the watcher to skip the write, on macOS and Windows, as before 0.1.4.
