# Obtainium-PC parity matrix

Tracks every upstream Obtainium (ImranR98) source and feature against the
WinUI 3 port. Statuses:

- **ported** - functionally equivalent on Windows
- **mapped** - ported with a documented Windows-specific mapping
- **track-only** - version/link tracking works on Windows; install is disabled
  and visibly labeled because the artifact is an Android package
- **pending** - not yet implemented
- **blocked** - truly nonportable; awaits an explicit user decision

## Sources

| Upstream source | Status | Notes |
| --- | --- | --- |
| GitHub | ported | releases, fallback to latest, filters, PAT, per-app creds |
| GitLab | ported | async prefetch modifier for project download URLs |
| Codeberg / Forgejo | ported | delegates to the GitHub pipeline with the Forgejo API host |
| SourceForge | mapped | RSS feed; suppress-standard-version-extraction flag |
| HTML | ported | intermediate links, link grabbing, pseudo-versioning (ETag / link hash / partial hash), release-date scraping |
| SourceHut | ported | git refs scraping (no GraphQL) |
| Direct File Link | ported | direct_apk_link.dart |
| NeutronCode | ported | single-file download pages |
| itch.io | mapped | selects Windows uploads (icon-windows8); itch.io's own "no thanks" free-download path; signed Cloudflare R2 URLs resolved at download time, never persisted |
| GitHub Stars (githubstars) | ported | bulk import with review picker; 100/page, rate-limit checked, PAT used |
| Jenkins | ported | last successful build artifacts, build number as version, neverAutoSelect picker |
| F-Droid (fdroid) | track-only | ported pipeline; install disabled with label; ABI nativecode filtering not ported (Android-only) |
| F-Droid third-party repo (fdroidrepo) | track-only | index-v2.json + legacy index.xml; stable-channel preference; install disabled with label |
| IzzyOnDroid | pending | Android packages - proposed track-only |
| APKMirror | pending | Android packages - proposed track-only |
| APKPure | pending | Android packages - proposed track-only |
| APKCombo | pending | Android packages - proposed track-only |
| APK4Free | pending | Android packages - proposed track-only |
| LiteAPKs | pending | Android packages - proposed track-only |
| Aptoide | pending | Android packages - proposed track-only |
| Uptodown | pending | Android packages - proposed track-only |
| CoolApk | pending | Android packages - proposed track-only |
| Farsroid | pending | Android packages - proposed track-only |
| RockMods | pending | Android packages - proposed track-only |
| RuStore | pending | Android packages - proposed track-only |
| Huawei AppGallery | pending | Android packages - proposed track-only |
| Samsung Galaxy Store | pending | Android packages - proposed track-only |
| Vivo App Store | pending | Android packages - proposed track-only |
| Tencent App Store | pending | Android packages - proposed track-only |
| Telegram App | pending | Android packages - proposed track-only |

"Proposed track-only" means: port the version/link pipeline fully, disable
the install action with a visible "Android package - cannot install on
Windows" label. Final treatment is the user's decision at the next review;
nothing is silently omitted.

## Features

| Upstream feature | Status | Notes |
| --- | --- | --- |
| Add app by URL | ported | source detection, standardization, override source |
| Per-app options | ported | full combinedAppSpecificSettingFormItems incl. update-health overrides |
| Background update checks | mapped | interval timer + check-on-start (no OS push; honors exemptFromBackgroundUpdates) |
| Update notifications | mapped | toast notifications, best-effort unpackaged |
| Installed-version detection | mapped | Windows registry Uninstall keys; unique name+publisher match required, ambiguous matches never drive actions |
| Downloads | ported | interactive download + open; no silent/elevated installs |
| Update Health page | ported | detection thresholds, flagged/ignored lists, ignore actions |
| Settings page | mapped | theme, interval, min age, global regex, source PATs |
| Import / Export | ported | upstream schema v2, three credential levels, same-ID replace |
| Apps list search/filter | mapped | name/author/URL query + updates-only toggle; categories pending | |
| Apps list categories/groups | pending | |
| Logs page | ported | JSONL store, 7-day retention, day filter, copy, clear |
| Crowdsourced app configs | pending | source presets from the community repo |
| Localization infra | pending | English default + language picker |
| APK installer actions | blocked | Android-only; install stays interactive-only on Windows |
| Shizuku/ADB/root install methods | blocked | Android-only |
| OS app settings (gear) | mapped | packaged apps only (PFN); honestly disabled otherwise |
