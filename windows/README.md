# Obtainium-PC (Windows)

Native Windows (WinUI 3) rebuild of [Obtainium](https://github.com/ImranR98/Obtainium),
the Android app updater by ImranR98. All credit for the original idea, design and
source app goes to the original author. This fork keeps the GPL-3.0 license.

The original Flutter/Android code is preserved unchanged at the repository root.
The Windows app lives in this `windows/` directory and is built in CI on
`windows-latest` (see `.github/workflows/build-windows.yml`).

Status: early scaffold - NavigationView shell (Apps / Add App / Import-Export /
Settings) with Mica backdrop. Feature parity with the original is the goal;
per-feature Windows mappings are being reviewed before implementation.

brought to you by Instinct
