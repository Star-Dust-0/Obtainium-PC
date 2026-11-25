# Obtainium-PC 2.0

Obtainium-PC is a native Windows (WinUI 3) app that tracks apps by their release pages, tells you when a new version is out, and downloads it. It is based on the idea and design of Obtainium by ImranR98.

## New
- **Portable ZIP apps.** Apps released as ZIP files are extracted into one stable folder per app under C:\Obtainium. A new version replaces the old one in place. Your own files and edited settings are kept, and a failed update is rolled back to the previous version.
- **Link to installed program.** In an app's "..." menu, pick the installed Windows program that is this app, so its installed version is read from it. Resetting the install clears the link.
- **Clear update status.** When a check fails, the app shows why (for example a server error or the GitHub rate limit). The summary on the Apps page names apps with updates, failed checks and apps not checked yet, and says "all up to date" only when that is true.
- **Clearer download status.** The download strip shows what is happening after the transfer (checking the file, saving, extracting, opening the installer) and reports a failure as a failure. Downloads are checked against the release's SHA-256 file when one is published.
- **Serial bulk installs.** Installers started from a bulk download run one after another.
- **Restorable export.** Exports contain your tracked apps and settings, including GitHub and other tokens, headers and credentials, so you can restore them later. Keep exported files private.
- **Settings import.** Importing applies the settings block of an export after confirmation. If an import changes an app's source address, the dialog lists the old and new address first.
- **Add page:** collapsible advanced options (asset filter, pre-releases).
- **About page** shows the app icon and the version.

## Fixes
- An app is no longer shown as having an update before its latest version is known, and a failed check no longer looks like an update.
- Installed-app detection is more accurate: Windows updates and hotfixes are skipped, 32-bit and 64-bit entries are merged, and an app you confirmed as installed stays matched, so Notepad no longer matches Notepad++.
- A final release is now newer than its own pre-release when comparing versions.
- A changed update interval applies right away, without restarting.
- A malformed record in apps.json no longer takes the other apps down. Unreadable records are kept in a side file.
- Very large version numbers compare correctly. A bad version regex is rejected in Settings instead of failing later.
- Stored GitHub tokens and per-app credentials are encrypted on disk.
- GitHub requests are cached, with a clear message when the rate limit is hit.
- Downloads use safe file names, and each app gets its own download folder. Cancelling a download removes its partial file.
- Options save while you type and when you leave the page, and an app is re-checked after an option changes.
- Options that nothing used are no longer shown. Your stored values are kept.

## Downloads
- Obtainium-PC-v2.0.0-win-x64-Setup.exe: installer, with an optional desktop shortcut. Needs administrator permission.
- Obtainium-PC-v2.0.0-win-x64-portable.zip: unzip anywhere and run ObtainiumPC.exe.
- SHA256SUMS.txt: checksums of both files.

Requires Windows 10 1809 or newer, 64-bit.

## Feedback
Please report problems at https://github.com/Star-Dust-0/Obtainium-PC/issues.
