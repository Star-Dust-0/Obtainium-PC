# Obtainium-PC v1.0

Obtainium-PC is a native Windows (WinUI 3) app that tracks apps by their release pages, tells you when a new version is out, and downloads it. It is based on the idea and design of Obtainium by ImranR98.

## Installer and updates
- Fixed: an app was offered an update when the installed version was the same release written differently (6.3.0.0 vs v6.3, 3.8.7 vs v.3.8.7).
- The installer now installs to `C:\Program Files\Obtainium-PC` and asks for administrator permission, as usual for that location.
- The installer ends a running Obtainium-PC (including one hidden in the tray) by itself, so the in-app update works.
- Installing over an existing per-user install moves it to Program Files. Your apps and settings are kept, and no duplicate entry or folder is left behind.
- The in-app update opens the installer through Windows, so the permission prompt appears when needed.

## Downloads
- Obtainium-PC-v1.0-win-x64-Setup.exe: installer, with an optional desktop shortcut.
- Obtainium-PC-v1.0-win-x64-portable.zip: unzip anywhere and run ObtainiumPC.exe.
- SHA256SUMS.txt: checksums of both files.

Requires Windows 10 1809 or newer, 64-bit.

## Feedback
Obtainium-PC is new. Please report problems at https://github.com/Star-Dust-0/Obtainium-PC/issues.
