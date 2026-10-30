# 0003 — Windows UI: WinUI 3, WPF as fallback

Status: accepted

**Decision.** Build the Windows app with WinUI 3. Fall back to WPF only if WinUI 3 blocks a requirement (tray, popup positioning, RTL).

**Why.** WinUI 3 gives native Windows 11 look and behavior, which the build guide requires.

**Consequences.** Packaging uses MSIX (step 5.3). The tray icon uses H.NotifyIcon. Any switch to WPF needs a new record.
