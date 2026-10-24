# Obtainium-PC parity matrix

Upstream reference: ImranR98/Obtainium at commit
`24807b46a9b2100eabd4c2dd357d84ded0027201` (2026-10-18, the fork's last
upstream sync). Test expectations are derived from that code, not from this
port's own implementation.

Source totals: 30 upstream sources; all 30 have a Windows port
(11 ported + 2 mapped + 17 track-only Android stores). Final treatment
of track-only Android packages is still pending the user's decision.

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
| IzzyOnDroid | track-only | F-Droid-compatible API on Izzy hosts; install disabled with label |
| APKMirror | track-only | app_exists API + RSS feed, release scrape; direct downloads forbidden upstream (PR #44) |
| APKPure | track-only | version-history API, variant grouping, arch suffixes |
| APKCombo | track-only | per-variant current-build links, R2 presigned URLs re-scraped at download; install disabled with label |
| Apk4Free | track-only | two-page scrape with title cleaning and version fallbacks; install disabled with label |
| LiteAPKs | track-only | WordPress API, Referer header + time-based CDN token at download; install disabled with label |
| Aptoide | track-only | page-scraped app ID + ws2 API; install disabled with label |
| Uptodown | track-only | page scrape + eAPI HMAC session, download prefetch, search |
| CoolApk | track-only | signed API requests |
| Farsroid | track-only | download-box API + shared HTML link grabber; install disabled with label |
| RockMods | track-only | JSON-LD version tracking only (track-only upstream too, PR #2966) |
| RuStore | track-only | HMAC device session, split-set mapping, search |
| Huawei AppGallery | track-only | front2 handshake, zone hosts, signed API, search |
| Samsung Galaxy Store | track-only | VAS stub + ODS grants, store-details enrichment |
| Vivo App Store | track-only | h5 detail API; install disabled with label |
| Tencent App Store | track-only | simple.jsp embedded-JSON scrape; install disabled with label |
| Telegram App | track-only | channel-page version scrape, fixed download URL; install disabled with label |

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

## UI page parity (mock review vs lib/pages/*.dart)

Reviewed by extracting each upstream page's user-facing features (its localized
action/label vocabulary) and mapping them to the WinUI pages. Statuses: full /
partial / gap. Interaction-verified items cite the ui-smoke evidence.

| Page | Status | Present in port | Known gaps vs upstream |
| --- | --- | --- | --- |
| Apps (apps.dart) | partial | search box, updates-only toggle, health indicator, refresh, sort by (as added/name/author/release date) + asc/desc per upstream AppListBuilder.sort, app card -> detail (ui-smoke verified), selection mode with batch pin/disable/mark-updated (confirm + non-standard-detection gate per upstream showMassMarkDialog)/remove/copy-URLs/copy-config-links/export-selected/categorize/download-with-per-app-asset-picker, category filter dropdown (upstream filter dialog simplified, disclosed), pin updates + bury non-installed reorder, list density (standard/compact/dense row metrics) | Windows share sheet (clipboard used), category colors/management UI |
| App detail (app.dart) | partial | installed/latest/release date, changelog dialog via tappable release date (URL-only changelogs shown as link; markdown shown as plain text, not rendered), Install-vs-Update action button matching upstream hasAction (enabled whenever installed differs from latest), asset picker on every manual download per upstream confirmAppFileUrl (apkUrls + otherAssetUrls, preferred/filter preselect), download with progress + safe completion, mark as updated, enable/disable tracking, reset install status, remove, per-app options, release page link, repo-rename banner with Update URL / dismiss (detection via GitHub redirect check in update checks), update-health note linking to the health page (ignore/restore ported there), track-only + pseudo-version notes, OS gear honestly disabled unpackaged (ui-smoke verified) | markdown changelog rendering (plain text shown); signer-change warning is Android-APK-certificate-specific - recorded N/A on Windows (no APK signing identity to verify) |
| Add App (add_app.dart) | partial | URL add, override source picker, track-only toggle, add-without-check fallback (settings toggle, placeholder app with upstream temp ID), already-added dedupe error (ui-smoke verified) crowdsourced configs link (opens apps.obtainium.imranr.dev), source search UI with results -> URL fill (GitHub + GitLab) | source search for F-Droid, Codeberg, Huawei, RuStore, Uptodown (GitHub + GitLab ported) |
| App options (app_additional_options.dart) | partial | combined per-source form items render (ui-smoke verified: GitHub release options) | verify every control kind (choice lists, headers) across sources |
| Settings (settings.dart) | partial | theme + language picker, update interval, min update age, check-on-start, global version/asset regexes, source PATs, update-health thresholds, app sort column + order, pin updates + bury non-installed toggles, add-without-check toggle, app list density, about section with upstream attribution + links (ui-smoke verified for settings render) | app list density, phone layout, background-update limits explanation, app-verifier sharing |
| Import / Export (import_export.dart) | partial | schema v2 export with 3 credential levels, import with same-ID replace (unit-tested roundtrip), URL-list import with per-URL error report auto-export on changes (debounced apps-only export to a configured path) | import from URLs-in-file / downloads folder, export-installed-only filter, export dir picker |
| Update Health (update_health.dart) | full | thresholds, flagged + ignored lists, ignore-for-version / ignore-permanently / restore (ui-smoke verified render) | none identified |
| Logs (logs.dart) | full | day-range filter, copy all, clear, JSONL store (ui-smoke verified render) | level filter, share (minor) |
| Home / navigation (home.dart) | full | nav pane to all pages, welcome -> add-app flow covered by Add App page | raw-JSON add (upstream home dialog, minor) |
