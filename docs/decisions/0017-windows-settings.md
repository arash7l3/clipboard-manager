# 0017 — Windows settings: a WinUI window over a JSON file, Run-key login item, language by restart

Status: accepted

**Decision.**

- **Window.** Settings… in the tray menu opens one window with three tabs, General, History and Privacy, as on the Mac (decision 0013). Each control changes its setting at once; there is no OK button. The window is a WinUI 3 `Window` (`SettingsWindow`) that hides when closed. The tabs are a `SelectorBar`, as in the popup.
- **Storage.** All settings except the login item are in `%LOCALAPPDATA%\Clipo\settings.json` (`AppSettings`, from step 3.3). Missing values keep their defaults, values out of range are brought back into range, and a file that cannot be read means the defaults. `App` is the only place that applies and saves a change; the window only reports it (`Changed`), so the tray menu's Paste After Choosing and the window cannot disagree.
- **Settings.** Start at sign-in, appearance (match Windows, light, dark), language (match Windows, English, Farsi), the shortcut, auto-paste, items to keep (50 to 1000, default 200), auto-clear (never, 1, 7 or 30 days), private mode, and the user's list of ignored apps (typed or picked with a file picker, kept as executable names). The built-in password-manager list stays on and is not editable.
- **Applying.** A new shortcut is registered at once. Private mode and ignored apps reach the clipboard listener at once (it reads `App._rules` before it reads any data) and the core. A change of limit, auto-clear, private mode or ignored apps gives the open history its limits and rules and deletes what has expired (`HistorySettings.Apply`, off the UI thread, one at a time, each with the settings as they are when it starts). Expired items are also deleted once an hour and when the history is opened.
- **Start at sign-in.** A value in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (`LoginItem`), which an unpackaged app can write without administrator rights. The command ends with `--background`, so the copy Windows starts shows no status window. If the user switched it off in Task Manager or Windows Settings (the `StartupApproved` key), Settings says so and opens the Startup page. At every launch an existing entry is pointed at the running executable, which keeps it working after the folder is moved or the app updated, without undoing a switch-off.
- **Theme.** `RequestedTheme` on the root of each window; it changes at once. The title bar's buttons of the status and settings windows keep following Windows.
- **Language.** Chosen language is applied before the first string is loaded: `ApplicationLanguages.PrimaryLanguageOverride` for the resources (`Strings\en-US`, `Strings\fa`) and the UI culture for the code. Because that is read once, Settings offers Restart, which starts a copy with `--restarted` and quits; the new copy waits up to ten seconds for the single-instance lock. With Farsi, every window's root gets `FlowDirection.RightToLeft` (WinUI does not take it from the language), and the tray menu and the popup's placement already followed the language (decisions 0015 and 0016).
- **Shortcut.** One of four modifier sets on the V key: Ctrl+Shift (default), Ctrl+Alt, Alt+Shift, Ctrl+Alt+Shift. If another app holds the chosen one, Clipo beeps, registers the old one again and Settings puts the choice back and says why.

**Why.**

- Saving in one place, from one handler, keeps the file, the running app and the window in step with little code, and lets the settings model and its rules be unit tested on any OS.
- A fixed list of shortcuts needs no key-recording control, which is easy to get wrong for non-Latin layouts and for keys Windows already uses (Win+V, Win+Shift+V).
- The `Run` key needs no package identity or administrator rights. `StartupTask` and Task Scheduler need one or the other.
- A restart is the only way to change the language of loaded resources reliably, and it is what the macOS app does.

**Consequences.**

- Changing the language needs a restart; until then the window stays in the old one.
- Ctrl+Alt combinations can clash with AltGr on some keyboard layouts; they are offered anyway because Windows reports the clash by refusing the registration.
- Private mode is a Settings switch only. A tray toggle belongs with the extras in phase 4.
- The ignored apps are matched by executable file name, as the capture already does (decision 0015); two programs with the same file name cannot be told apart.
- Like the rest of the Windows app, compiled and unit-tested in CI, not yet run on a Windows PC. Whether `PrimaryLanguageOverride` takes effect in this unpackaged, self-contained app is the first thing to check there.
