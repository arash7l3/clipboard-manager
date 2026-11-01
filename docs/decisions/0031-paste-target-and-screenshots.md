# 0031 — Pasting into the app that was in front, and screenshots in the history

Status: accepted. Amends decisions 0012, 0010 and 0028; corrects the reasoning of 0030.

**Decision.**
- **macOS, files and images:** after a file or image item is written to the clipboard, the app in front resigns and regains active status before Cmd+V: Clipo activates for a moment and yields back to it (`NSApp.activate`, `yieldActivation(to:)`, `activate(from:)`). This happens whether Clipo pastes or the user does. Text pastes as before.
- **macOS, Cmd+V:** the synthetic key events carry the left Command key's device flag (`NX_DEVICELCMDKEYMASK`) beside `.maskCommand`, as a real key press does.
- **macOS, screenshots:** `ScreenshotWatcher` watches the folder the Screenshot app saves to (`com.apple.screencapture` `location`, the Desktop by default) and adds each new file marked `com.apple.metadata:kMDItemIsScreenCapture` as an image, credited to `com.apple.screencaptureui`. Private mode and the ignored apps apply; ignoring that id keeps screenshots out. Info.plist explains the Desktop, Documents and Downloads access prompts.
- **Windows:** the popup gives the focus back to `ForegroundTracker.AppWindowInFront()`: the window in front if it is an app's, otherwise the last app window that was. A window counts as an app's if it belongs to another process, is shown, is not a tool window and is not a known shell surface.

**Why.** Checks on the build Mac (macOS 27) and on a hosted Windows Server 2025 runner showed:
- On macOS, picking a copied folder over Finder wrote it to the clipboard, and Clipo's Cmd+V reached Finder (the window server routed a keyboard event to Finder 150 ms after the popup closed, after Clipo was allowed to post events), but Finder pasted nothing; the same event pasted text in Chrome. A standard AppKit menu matches the event under both the ABC and Persian layouts, so the key itself is fine. The popup never activates Clipo, so Finder never stops being the active app and does not look at the pasteboard again; activating it again is what a copy made in another app normally causes. Clipy and Maccy, two widely used clipboard managers, post Cmd+V with the device flag.
- Decision 0030 blamed Finder's file reference URLs. A probe app without Desktop access read them as plain paths and wrote them back correctly, so that was not the cause; its safeguards stay.
- The Mac's screenshot setting was `target: file`: screenshots never reach the clipboard.
- On Windows, a test drove Clipo's tray, menu and focus code with real input: Notepad focused, the tray icon right-clicked in the hidden-icons flyout, a snippet picked. The snippet reached Notepad. The same run showed that when the shell reports the click, the window in front is the flyout (`TopLevelWindowForOverflowXamlIsland`, a tool window of explorer.exe), and the popup opened from the tray icon remembered exactly that window, so a pick there gave the focus to the taskbar.

**Consequences.**
- Picking a file or image item makes the menu bar flicker once while Clipo is active.
- The first screenshot watch asks the user for access to the Desktop (or the Save To folder). Declined, screenshots stay out and the log says the folder cannot be watched. HEIC and PDF screenshots are not kept; Clipo stores PNG, JPEG, TIFF and BMP.
- The Finder cause could not be confirmed end to end, because CI cannot press keys on the build Mac; if a picked file still does not paste into Finder, the next check is a test with the Accessibility permission on that Mac.
