# Obtainium-PC parity matrix

Upstream reference: ImranR98/Obtainium at commit
`24807b46a9b2100eabd4c2dd357d84ded0027201` (2026-10-18, the fork's last
upstream sync). Test expectations are derived from that code, not from this
port's own implementation.

Source totals: 30 upstream sources; all 30 have a Windows port
(9 ported + 2 mapped + 19 track-only Android stores). Track-only is the
final, settled treatment for the 19 Android-package sources (decision
recorded below); "30 sources represented" never means "30 installable".

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
| GitHub | ported | releases, fallback to latest, filters, PAT, per-app creds; asset acceptance mapped from upstream's APK container list to Windows installers (.exe/.msi/.msix/.appx/.appinstaller) - a repo with no Windows-installer assets fails the check with no-releases, the same structural filter upstream applies to repos with no APK assets (github.dart:713/830/876) |
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
| Vivo App Store | track-only | h5 detail API, search; install disabled with label |
| Tencent App Store | track-only | simple.jsp embedded-JSON scrape; install disabled with label |
| Telegram App | track-only | channel-page version scrape, fixed download URL; install disabled with label |

Track-only is the final treatment for these 19 sources (decided 2026-10-25):
the version/link pipeline is ported fully - tracking, update checks,
notifications, changelogs, per-app options, and downloading the APK to the
user's downloads folder for manual transfer all work. The install action is
disabled with a visible "Android package - cannot install on Windows" label.
These sources only serve Android APK packages through the pipelines upstream
uses; none offers a Windows binary, so install/run/registry-detection is
technically impossible on Windows rather than an omitted feature. Nothing is
silently omitted.

## Features

| Upstream feature | Status | Notes |
| --- | --- | --- |
| Add app by URL | ported | source detection, standardization, override source |
| Per-app options | ported | full combinedAppSpecificSettingFormItems incl. update-health overrides |
| Background update checks | mapped | interval timer + check-on-start (no OS push; honors exemptFromBackgroundUpdates) |
| Update notifications | mapped | toast notifications, best-effort unpackaged |
| Installed-version detection | ported | Windows registry Uninstall keys; conservative matcher (exact / prefix / 60%-containment tiers, publisher disambiguation, ambiguous never drives actions); runs on every update check + Detect-now button on detail; unit-tested |
| External-uninstall reconcile | ported (conservative) | lib/providers/apps_provider_lifecycle.dart reconcileInstallStatus: at app load, a tracked app whose recorded install is gone from the OS gets its recorded version cleared. Windows safety rules: only OS-confirmed installs (provenance recorded when registry detection adopted/corrected the version; manual marks and legacy records never cleared); when the exact DisplayName matched at confirmation time is absent from a COMPLETE three-hive registry probe, the conservative app-name matcher re-runs first - a Strong+ match means the install was renamed/updated and its new identity is re-adopted instead of cleared (absence alone is never treated as proof); detection gating respected (track-only, versionDetection off, source-disallowed detection preserve the record); any probe failure or ambiguity preserves recorded state. Unit-tested decision layer including rename/update transitions. Upstream's optional destructive half (removeOnExternalUninstall deleting the tracking record, default off upstream) is NOT implemented and remains held - no approval exists for record deletion; records are always retained |
| Downloads | ported | interactive download + open; no silent/elevated installs |
| Update Health page | ported | detection thresholds, flagged/ignored lists, ignore actions |
| Settings page | mapped | theme, interval, min age, check-on-start, notifications, hide-downgrades, add-without-check, global regexes, source PATs, update-health thresholds, sort column+order, pin/bury toggles, list density, category registry with colors, repo-rename detection, about section with attribution |
| Import / Export | ported | upstream schema v2, three credential levels, same-ID replace |
| Apps list search/filter | ported | name/author/URL query + updates-only toggle + category filter dropdown + full filter dialog (AppsFilter: name/author/id text, include up-to-date/non-installed, category multi-select, source dropdown - lib/components/app_list_tile.dart) |
| Apps list categories/groups | ported | per-app categories, category registry with colors + Settings management, batch categorize, filter dropdown, row labels; collapsible group headers (none/category/source grouping, per-session collapse, collapse-on-startup toggle) |
| Logs page | ported | JSONL store, day filter, copy, share (native share sheet), clear, level colors |
| Crowdsourced app configs | ported | Add App links to apps.obtainium.imranr.dev - matches the pinned upstream exactly (add_app.dart openCrowdsourcedConfigs is itself an external-browser launch; no in-app picker exists upstream). Config sharing via generated redirect links is ported in the Apps batch share |
| Localization | ported | all 30 upstream locales (lib/main.dart supportedLocales) reused mechanically from the preserved assets/translations/*.json via the checked-in generator windows/tools/gen_translations.py -> LocTranslations.g.cs (11k+ entries, keyed by upstream en.json values = the port's gettext literals), follow-system default with device-locale resolution (exact tag, then language prefix, zh-Hant handling, English fallback; unit-tested), language picker in Settings, RTL for ar + fa (flow direction flips incl. popup trees (ThemedContentDialog mirrors FlowDirection), light/dark + language round trips ui-smoke verified, run 40149527737), port-only Arabic strings overlaid by hand. plural forms are reused from the same files with per-locale CLDR category selection (PluralRules, unit-tested 0/1/2/few/many/other incl. the Arabic ladder and easy_localization-style missing-form fallback to other; LocPlurals.g.cs generated, wired at the port's count call sites - group header counts, batch-remove title matching apps_provider_lifecycle.dart:516); port-only strings in non-Arabic languages stay English - disclosed |
| APK installer actions | blocked | Android-only; install stays interactive-only on Windows |
| Shizuku/ADB/root install methods | blocked | Android-only |
| OS app settings (gear) | mapped | packaged apps only (PFN); honestly disabled otherwise |

## UI page parity (mock review vs lib/pages/*.dart)

Reviewed by extracting each upstream page's user-facing features (its localized
action/label vocabulary) and mapping them to the WinUI pages. Statuses: full /
partial / gap. Interaction-verified items cite the ui-smoke evidence.

| Page | Status | Present in port | Known gaps vs upstream |
| --- | --- | --- | --- |
| Apps (apps.dart) | partial | search box, updates-only toggle, health indicator, refresh, sort by (as added/name/author/release date) + asc/desc per upstream AppListBuilder.sort, app card -> detail (ui-smoke verified), selection mode with batch share-URLs/share-config-links via native Windows share sheet (DataTransferManager, clipboard fallback), pin/disable/mark-updated (confirm + non-standard-detection gate per upstream showMassMarkDialog)/remove/copy-URLs/copy-config-links/export-selected/categorize/download-with-per-app-asset-picker, category filter dropdown (upstream filter dialog simplified, disclosed), category registry with colors (random light color, delete prunes from apps per upstream setCategories, management in Settings, categories shown on app rows), pin updates + bury non-installed reorder, list density (standard/compact/dense row metrics) | upstream filter dialog simplified (disclosed) |
| App detail (app.dart) | partial | installed/latest/release date, changelog dialog via tappable release date (URL-only changelogs shown as link; markdown rendered via built-in renderer: headings, bold/italic, inline + fenced code, links, images, pipe tables, blockquotes, horizontal rules, basic HTML tag stripping), Install-vs-Update action button matching upstream hasAction (enabled whenever installed differs from latest); track-only apps get a Download-labeled primary action (the port keeps the authorized manual-download flow, so the label must not promise installation) plus the upstream markInstalled/markUpdated bookkeeping action as separate Mark installed/Mark updated command-bar actions recording installedVersion = latestVersion with no download or install (lib/pages/app.dart:508/511, same hasAction gating; reset-install takes over once installed matches latest, lib/pages/app.dart:600), asset picker on every manual download per upstream confirmAppFileUrl (apkUrls + otherAssetUrls, preferred/filter preselect), download with progress + safe completion, mark as updated, enable/disable tracking, reset install status, remove, per-app options, release page link, repo-rename banner with Update URL / dismiss (detection via GitHub redirect check in update checks), update-health note linking to the health page (ignore/restore ported there), track-only + pseudo-version notes, OS gear honestly disabled unpackaged (ui-smoke verified) | flutter_markdown_plus corners retained: footnotes and full raw-HTML rendering stay disclosed (raw HTML tags strip to plain text, <summary> labels render bold); the rendered set now covers headings, bold/italic/bold-italic (star and underscore forms), strikethrough, inline + fenced code, links, bare-URL autolinks, images, pipe tables, blockquotes, horizontal rules, GFM task-list items and indent-nested lists (MarkdownInlineParser unit-tested); signer-change warning is Android-APK-certificate-specific - recorded N/A on Windows (no APK signing identity to verify) |
| Add App (add_app.dart) | partial | URL add, override source picker, track-only toggle, add-without-check fallback (settings toggle, placeholder app with upstream temp ID), already-added dedupe error (ui-smoke verified) crowdsourced configs link (opens apps.obtainium.imranr.dev), source search UI with results -> URL fill (all 9 upstream searchable sources: GitHub, GitLab, Codeberg, F-Droid, F-Droid Third-Party Repo, Uptodown, RuStore, Huawei AppGallery, Vivo App Store), per-search settings dialog for sources with includeAdditionalOptsInMainSearch (repo/instance URL, Codeberg token + min stars per upstream searchQuerySettingItemsForUrl) | dialog only appears for the 2 upstream sources that set the flag, matching upstream |
| App options (app_additional_options.dart) | full | combined per-source form items render (ui-smoke verified at runtime: GitHub release options, F-Droid and Farsroid per-source editors render with their upstream labels, run 40149527737); per-source key sweep verified every upstream form key is exposed (added the 8 missing editors on F-Droid / F-Droid repo / Farsroid); Slider maps to a choice list with identical stored values; SubForm (HTML intermediateLink, requestHeader) maps to codec-backed text editing the same JSON | none identified |
| Settings (settings.dart) | partial | theme, language picker (all 30 upstream locales + follow system), update interval, min update age, check-on-start, obtainium:// links toggle, global version/asset regexes + match group, GitHub PAT + proxy request prefix, repo-rename detection toggle, source PATs, update-health thresholds, app sort column + order, pin updates + bury non-installed toggles, add-without-check toggle, app list density, group-by + collapse-on-startup, auto-export on changes, check-update-on-detail-page (single-app refresh on detail open, lib/pages/app.dart:1252), only-check-installed-and-track-only-apps (check candidate filter, lib/providers/apps_provider_updates.dart:132), include-prereleases-by-default (prefills the per-app includePrereleases option at add time, lib/pages/add_app.dart:628), track-only add confirmation with dont-show-again (hideTrackOnlyWarning, lib/pages/add_app.dart:213), show-app-downgrade-errors toggle, skip-update/install-confirmation toggle, allow-parallel-downloads toggle, enable-certificate-pinning toggle, about section with upstream attribution + links (ui-smoke verified for settings render) | phone layout (Android N/A), background silent installs + limits explanation (Android silent-APK N/A - Windows installs are interactive by design), app-verifier sharing (Android APK-signature N/A), showAppDowngradeError ported with a Windows-safe comparison (numeric version compare when comparable, incomparable versions pass through; blocks downgrading updates on detail + batch paths, default on), enableCertificatePinning ported (upstream's bundled CA roots linked from assets/ca-certs; pinned hosts github.com/codeberg.org/gitlab.com/rustore.ru + subdomains trust only those roots; RuStore Mintsifry-root workaround always on, http_service.dart), parallelDownloads ported (batch downloads run concurrently with a combined progress dialog when on, serial per-app dialogs when off, default on; install launches serialize as downloads complete like upstream's install chain), skipBulkUpdateConfirmation ported (bulk confirm dialog listing the set before any download unless skipped, apps.dart:283, default off) |
| Import / Export (import_export.dart) | partial | schema v2 export with 3 credential levels, import with same-ID replace (unit-tested roundtrip), URL-list import with per-URL error report auto-export on changes (debounced apps-only export to a picked file path) , import URLs from a file (regex extraction, unsupported filtered), export-installed-only toggle | none significant (downloads-folder import ported against the app downloads dir; TV DocumentTree variant is Android-specific N/A) |
| Update Health (update_health.dart) | full | thresholds, flagged + ignored lists, ignore-for-version / ignore-permanently / restore (ui-smoke interaction-verified: ignore -> Ignored section -> restore round trip, run 40149527737) | none identified |
| Logs (logs.dart) | full | day-range filter, copy all, share via native Windows share sheet (clipboard fallback per upstream logs.dart), clear, level colors per upstream _levelColor, JSONL store (ui-smoke verified render) | none significant |
| Home / navigation (home.dart) | full | nav pane to all pages, welcome -> add-app flow covered by Add App page; raw-JSON app import via paste box on Import/Export AND via obtainium:// deep link: add (standardize, existing-app match, Add-App prefill), app/apps (raw-JSON confirmation dialog -> import), refresh?id= all ported from home.dart interpretLink (deep-link import list refresh and refresh-with-explicit-id UI-thread marshal both ui-smoke verified, run 40149527737). Unpackaged OS integration: scheme registered under HKCU\\Software\\Classes\\obtainium (no admin), link clicks arrive as command-line args, single-instance redirect hands them to the running window; Settings toggle disables/unregisters; supported-sources dialog ported (add_app.dart openSourcesListDialog) | none identified |
