using System;
using System.Collections.Generic;

namespace ObtainiumPC.Services.Localization;

/// <summary>
/// Gettext-style localization: the English literal is the key; untranslated
/// strings pass through unchanged. All upstream languages
/// (assets/translations/*.json, lib/main.dart supportedLocales) are reused
/// mechanically via the generated LocTranslations table; port-only Arabic
/// strings live in the ArOverlay below and take precedence for ar.
/// "" means follow the system locale (upstream forcedLocale = null);
/// unresolved/unknown locales fall back to English. RTL: ar, fa.
/// </summary>
public static class Loc
{
    /// "" = follow system (upstream forcedLocale null), else a supported tag.
    public static string Language = "";

    public static event Action? Changed;

    public static bool IsRtl => ResolvedLanguage is "ar" or "fa";

    /// The effective language tag after system-locale resolution.
    public static string ResolvedLanguage =>
        Language.Length > 0 ? Language : SystemLanguage();

    private static string SystemLanguage()
    {
        try
        {
            var tag = Windows.System.UserProfile.GlobalizationPreferences.Languages.Count > 0
                ? Windows.System.UserProfile.GlobalizationPreferences.Languages[0]
                : null;
            return LocaleResolution.Resolve(tag);
        }
        catch { return "en"; }
    }

    public static void SetLanguage(string language)
    {
        if (Language == language) return;
        Language = language;
        Changed?.Invoke();
    }

    public static string T(string en) => TranslateAs(en, ResolvedLanguage);

    private static readonly Dictionary<string, string> ReverseIndex = new();
    private static bool ReverseIndexBuilt;

    /// Recover the English key whose translation in a supported language
    /// equals the given text. Used when an element is first seen already
    /// translated (e.g. a recreated nav container that inherited the
    /// translated label). Ambiguous translations resolve to the first
    /// recorded key - cosmetic at worst.
    public static bool TryReverse(string text, out string key)
    {
        if (!ReverseIndexBuilt)
        {
            foreach (var (k, v) in ArOverlay) ReverseIndex.TryAdd(v, k);
            foreach (var (_, dict) in LocTranslations.ByLang)
                foreach (var (k, v) in dict)
                    ReverseIndex.TryAdd(v, k);
            ReverseIndexBuilt = true;
        }
        return ReverseIndex.TryGetValue(text, out key!);
    }

    /// Translate a key as if the given language were active (used to detect
    /// an element still showing the previous language's translation).
    public static string TranslateAs(string en, string language)
    {
        if (language.Length == 0 || language == "en") return en;
        if (language == "ar" && ArOverlay.TryGetValue(en, out var ar)) return ar;
        if (LocTranslations.ByLang.TryGetValue(language, out var dict)
            && dict.TryGetValue(en, out var v)) return v;
        return en;
    }

    /// <summary>
    /// Upstream trPlural: select the correct plural form for the active
    /// language (CLDR rules via PluralRules, missing forms fall back to
    /// "other" like easy_localization, then to English). enOtherLiteral is
    /// the English "other" value from upstream en.json - the port's gettext
    /// key. The "{}" or "{0}" placeholder in the selected form is replaced
    /// with the count.
    /// </summary>
    public static string Plural(string enOtherLiteral, int n)
    {
        var lang = ResolvedLanguage;
        string form;
        if (lang.StartsWith("ar", StringComparison.Ordinal) && ArPlurals.TryGetValue(enOtherLiteral, out var arForms))
        {
            form = PluralRules.SelectForm(arForms, lang, n, enOtherLiteral);
        }
        else if (LocPlurals.ByLang.TryGetValue(lang, out var table)
            && table.TryGetValue(enOtherLiteral, out var forms))
        {
            form = PluralRules.SelectForm(forms, lang, n, enOtherLiteral);
        }
        else if (LocPlurals.ByLang.TryGetValue("en", out var enTable)
            && enTable.TryGetValue(enOtherLiteral, out var enForms))
        {
            // Unknown language: English plural selection still applies.
            form = PluralRules.SelectForm(enForms, "en", n, enOtherLiteral);
        }
        else form = enOtherLiteral;
        return form.Replace("{}", n.ToString()).Replace("{0}", n.ToString());
    }

    // Port-only Arabic plural forms for keys upstream's ar.json does not carry (formal Arabic,
    // CLDR zero/one/two/few/many/other; missing forms fall back to "other").
    private static readonly Dictionary<string, Dictionary<string, string>> ArPlurals = new()
    {
        ["{} checks"] = new() { ["one"] = "فحص واحد", ["two"] = "فحصان", ["few"] = "{} فحوصات", ["many"] = "{} فحصًا", ["other"] = "{} فحص" },
        ["{} releases"] = new() { ["one"] = "إصدار واحد", ["two"] = "إصداران", ["few"] = "{} إصدارات", ["many"] = "{} إصدارًا", ["other"] = "{} إصدار" },
        ["{} update checks failed in a row"] = new()
        {
            ["one"] = "فشل فحص التحديث مرة واحدة متتالية", ["two"] = "فشل فحص التحديث مرتين متتاليتين",
            ["few"] = "فشل فحص التحديث {} مرات متتالية", ["many"] = "فشل فحص التحديث {} مرة متتالية", ["other"] = "فشل فحص التحديث {} مرة متتالية",
        },
        ["{} newer releases are hidden by your filters"] = new()
        {
            ["one"] = "إصدار أحدث واحد مخفي بسبب عوامل التصفية لديك", ["two"] = "إصداران أحدث مخفيان بسبب عوامل التصفية لديك",
            ["few"] = "{} إصدارات أحدث مخفية بسبب عوامل التصفية لديك", ["many"] = "{} إصدارًا أحدث مخفيًا بسبب عوامل التصفية لديك",
            ["other"] = "{} إصدار أحدث مخفي بسبب عوامل التصفية لديك",
        },
    };

    // Port-only Arabic strings (UI text with no upstream en.json entry).
    // Upstream ar.json entries come from the generated table instead.
    private static readonly Dictionary<string, string> ArOverlay = new()
    {
        // Navigation + page titles
        ["Apps"] = "التطبيقات",
        ["Add App"] = "إضافة تطبيق",
        ["Update Health"] = "سلامة التحديثات",
        ["Import / Export"] = "استيراد / تصدير",
        ["Logs"] = "السجلات",
        ["Settings"] = "الإعدادات",
        ["About"] = "حول",
        ["Appearance"] = "المظهر",
        ["Updates"] = "التحديثات",
        ["Categories"] = "الفئات",
        ["Changelog"] = "سجل التغييرات",
        ["Detection"] = "الاكتشاف",
        ["The apps list could not be read."] = "تعذّرت قراءة قائمة التطبيقات.",
        ["The settings file could not be read."] = "تعذّرت قراءة ملف الإعدادات.",
        ["The original files were kept untouched and changes will not be saved this session. Fix or restore the file, then restart the app."] = "تم الاحتفاظ بالملفات الأصلية دون تغيير ولن تُحفظ التغييرات في هذه الجلسة. أصلح الملف أو استعده ثم أعد تشغيل التطبيق.",
        ["Select an app to see its details."] = "اختر تطبيقًا لعرض تفاصيله.",
        ["A copy was kept at:"] = "تم حفظ نسخة في:",
        ["Stored data could not be read"] = "تعذّرت قراءة البيانات المخزّنة",
        ["Import"] = "استيراد",
        ["Export"] = "تصدير",
        ["Bulk import"] = "استيراد جماعي",
        ["Import from URL list"] = "استيراد من قائمة روابط",
        ["Flagged apps"] = "التطبيقات المُبلَّغ عنها",
        ["Ignored apps"] = "التطبيقات المتجاهَلة",
        ["The request failed. Details are in the logs."] = "فشل الطلب. التفاصيل في السجلات.",
        ["Back up, restore and move your tracked apps."] = "انسخ تطبيقاتك المتتبعة احتياطيًا واستعدها وانقلها.",
        ["Appearance, update behavior and sources."] = "المظهر وسلوك التحديث والمصادر.",
        ["Apps whose update checks need attention."] = "التطبيقات التي تحتاج فحوصات التحديث الخاصة بها إلى انتباه.",
        ["On"] = "مفعّل",
        ["Off"] = "معطّل",
        ["Version detection (global defaults)"] = "اكتشاف الإصدار (الافتراضيات العامة)",

        // Source errors (SourceErrorText)
        ["Releases were found, but none has a Windows installer asset (.exe, .msi, .msix, .appx, .appinstaller). Turn on track-only for this app to follow its version without installing."] =
            "تم العثور على إصدارات، لكن لا يحتوي أيٌّ منها على ملف تثبيت لنظام Windows (\u200E.exe\u060C \u200E.msi\u060C \u200E.msix\u060C \u200E.appx\u060C \u200E.appinstaller\u200E). فعّل «التتبع فقط» لهذا التطبيق لمتابعة إصداره دون تثبيته.",

        ["Auto-detect"] = "اكتشاف تلقائي",
        ["Server request failed (HTTP {0})"] = "فشل طلب الخادم (HTTP {0})",
        ["Invalid URL for source {0}"] = "رابط غير صالح للمصدر {0}",

        // Buttons and actions
        ["Add"] = "إضافة",
        ["Install"] = "تثبيت",
        ["Update"] = "تحديث",
        ["Options"] = "الخيارات",
        ["Remove"] = "إزالة",
        ["App settings"] = "إعدادات التطبيق",
        ["Disable tracking"] = "تعطيل التتبّع",
        ["Enable tracking"] = "تفعيل التتبّع",
        ["Mark as updated"] = "تعليم كمُحدَّث",
        ["Detect installed version now"] = "اكتشاف الإصدار المثبّت الآن",
        ["Open release page"] = "فتح صفحة الإصدار",
        ["Update health: view details"] = "سلامة التحديثات: عرض التفاصيل",
        ["Update URL"] = "تحديث الرابط",
        ["Dismiss"] = "تجاهل",
        ["Delete"] = "حذف",
        ["Select"] = "تحديد",
        ["Updates only"] = "التحديثات فقط",
        ["Search"] = "بحث",
        ["Share"] = "مشاركة",
        ["Copy all"] = "نسخ الكل",
        ["Clear logs"] = "مسح السجلات",
        ["Download"] = "تنزيل",
        ["Filter"] = "تصفية",
        ["App name"] = "اسم التطبيق",
        ["Author"] = "المؤلف",
        ["App ID"] = "معرّف التطبيق",
        ["Up to date apps"] = "تطبيقات محدثة",
        ["Non-installed apps"] = "تطبيقات غير مثبتة",
        ["Source"] = "المصدر",
        ["Apply"] = "تطبيق",
        ["Clear"] = "مسح",
        ["Cancel"] = "إلغاء",
        ["Continue"] = "متابعة",
        ["Don't show this again"] = "لا تظهر هذا مرة أخرى",
        ["App is track-only"] = "التطبيق للتتبع فقط",
        ["You have selected the 'track-only' option."] = "لقد اخترت خيار «التتبع فقط».",
        ["The app will be tracked for updates; downloads are manual and cannot be installed on Windows."] = "سيتم تتبع التطبيق للتحديثات؛ التنزيلات يدوية ولا يمكن تثبيتها على Windows.",
        ["Download"] = "تنزيل",
        ["Downloads"] = "التنزيلات",
        ["OK"] = "حسنًا",
        ["Downgrade blocked"] = "تم حظر الرجوع لإصدار أقدم",
        ["the installed version"] = "الإصدار المثبت",
        ["is newer than the latest release"] = "أحدث من آخر إصدار",
        ["The update was blocked."] = "تم حظر التحديث.",
        ["OK"] = "حسنًا",
        ["Save"] = "حفظ",
        ["Browse..."] = "استعراض...",
        ["Export to file..."] = "تصدير إلى ملف...",
        ["Import from file..."] = "استيراد من ملف...",
        ["Import from downloads..."] = "استيراد من التنزيلات...",
        ["Paste raw JSON..."] = "لصق JSON خام...",
        ["Import URLs"] = "استيراد الروابط",
        ["From file..."] = "من ملف...",
        ["Import GitHub starred repos..."] = "استيراد المستودعات المميزة بنجمة من GitHub...",
        ["Browse crowdsourced app configs"] = "تصفّح إعدادات التطبيقات من المجتمع",
        ["GPL-3.0 license"] = "رخصة GPL-3.0",
        ["Upstream Obtainium (ImranR98)"] = "Obtainium الأصلي (ImranR98)",
        ["Obtainium-PC source (this fork)"] = "مصدر Obtainium-PC (هذا التفريع)",
        ["Track only (check versions without downloading)"] = "تتبّع فقط (فحص الإصدارات دون تنزيل)",
        ["System default"] = "افتراضي النظام",
        ["Light"] = "فاتح",
        ["Dark"] = "داكن",
        ["As added"] = "كما أُضيفت",
        ["Name (Author)"] = "الاسم/المؤلف",
        ["Author (Name)"] = "المؤلف/الاسم",
        ["Release date"] = "تاريخ الإصدار",
        ["Ascending"] = "تصاعدي",
        ["Descending"] = "تنازلي",
        ["Standard"] = "قياسي",
        ["Compact"] = "مدمج",
        ["Dense"] = "كثيف",
        ["15 minutes"] = "15 دقيقة",
        ["30 minutes"] = "30 دقيقة",
        ["1 hour"] = "ساعة واحدة",
        ["6 hours"] = "6 ساعات",
        ["12 hours"] = "12 ساعة",
        ["1 day"] = "يوم واحد",
        ["2 days"] = "يومان",
        ["3 days"] = "3 أيام",
        ["5 days"] = "5 أيام",
        ["7 days"] = "7 أيام",
        ["14 days"] = "14 يومًا",
        ["30 days"] = "30 يومًا",
        ["None"] = "بلا",
        ["Group apps by"] = "تجميع التطبيقات حسب",
        ["Collapse all groups on app start"] = "طيّ كل المجموعات عند بدء التشغيل",
        ["Category"] = "الفئة",
        ["Source"] = "المصدر",
        ["No category"] = "بلا فئة",
        ["No source"] = "بلا مصدر",
        ["{0} app"] = "تطبيق واحد",
        ["{0} apps"] = "{0} تطبيقات",
        ["Update available"] = "التحديث متاح",
        ["All"] = "الكل",
        ["Supported sources"] = "المصادر المدعومة",
        ["(track only)"] = "(تتبّع فقط)",
        ["(searchable)"] = "(قابل للبحث)",
        ["Paste raw JSON"] = "لصق JSON خام",

        // Settings labels
        ["Theme"] = "السمة",
        ["Language"] = "اللغة",
        ["Sort apps by"] = "ترتيب التطبيقات حسب",
        ["Sort order"] = "اتجاه الترتيب",
        ["App list density"] = "كثافة قائمة التطبيقات",
        ["Pin apps with updates to the top"] = "تثبيت التطبيقات ذات التحديثات في الأعلى",
        ["Move apps not yet installed to the bottom"] = "نقل التطبيقات غير المثبّتة إلى الأسفل",
        ["Background update interval"] = "الفاصل الزمني للتحقق من التحديثات",
        ["Minimum age for updates (global default)"] = "الحد الأدنى لعمر التحديثات (الافتراضي العام)",
        ["Check for updates on app start"] = "التحقق من التحديثات عند بدء التشغيل",
        ["Show update notifications"] = "إظهار إشعارات التحديثات",
        ["Hide downgrades (when installed version is newer)"] = "إخفاء الرجوع لإصدار أقدم (عندما يكون الإصدار المثبّت أحدث)",
        ["Offer to add apps without a version check when the source fails"] = "عرض إضافة التطبيقات دون فحص الإصدار عند فشل المصدر",
        ["Detect renamed repositories"] = "اكتشاف المستودعات المُعاد تسميتها",
        ["Filter assets by regular expression (global default)"] = "تصفية الملفات بتعبير نمطي (الافتراضي العام)",
        ["Trim version string with RegEx"] = "قصّ نص الإصدار بتعبير نمطي",
        ["Match group to use"] = "مجموعة المطابقة المستخدمة",
        ["Personal access token (raises API rate limits)"] = "رمز الوصول الشخصي (يرفع حدود معدل API)",
        ["Request prefix (for GitHub proxies)"] = "بادئة الطلب (لوسيط GitHub)",
        ["Flag after this many consecutive failed checks"] = "الإبلاغ بعد هذا العدد من الفحوصات الفاشلة المتتالية",
        ["Flag after this many newer releases were filtered out"] = "الإبلاغ بعد تصفية هذا العدد من الإصدارات الأحدث",
        ["Flag archived repositories"] = "الإبلاغ عن المستودعات المؤرشفة",
        ["Flag when the latest release is older than"] = "الإبلاغ عندما يكون أحدث إصدار أقدم من",
        ["Flag when the version has not changed for"] = "الإبلاغ عندما لا يتغيّر الإصدار لمدة",
        ["Show update-health indicator on the Apps list"] = "إظهار مؤشر سلامة التحديثات في قائمة التطبيقات",
        ["Only include installed apps in exports"] = "تضمين التطبيقات المثبّتة فقط في التصدير",
        ["What to include"] = "ما يتم تضمينه",
        ["Automatically export on changes"] = "التصدير تلقائيًا عند التغييرات",
        ["Auto-export file path"] = "مسار ملف التصدير التلقائي",
        ["Override source (optional)"] = "تجاوز المصدر (اختياري)",

        // Export choices
        ["Apps only"] = "التطبيقات فقط",
        ["Apps and settings (credentials stripped)"] = "التطبيقات والإعدادات (بدون بيانات الاعتماد)",
        ["Apps and settings, including credentials"] = "التطبيقات والإعدادات بما فيها بيانات الاعتماد",

        // Placeholders
        ["Search apps"] = "بحث في التطبيقات",
        ["Search for a project"] = "ابحث عن مشروع",
        ["GitHub username"] = "اسم مستخدم GitHub",

        // Log day ranges
        ["Last day"] = "آخر يوم",
        ["Last 2 days"] = "آخر يومين",
        ["Last 3 days"] = "آخر 3 أيام",
        ["Last 4 days"] = "آخر 4 أيام",
        ["Last 5 days"] = "آخر 5 أيام",
        ["Last 7 days"] = "آخر 7 أيام",

        // Descriptions and static text
        ["No apps yet. Use Add App to track your first app."] = "لا توجد تطبيقات بعد. استخدم \"إضافة تطبيق\" لتتبّع أول تطبيق.",
        ["No apps with update-health issues."] = "لا توجد تطبيقات بمشكلات في سلامة التحديثات.",
        ["No ignored apps."] = "لا توجد تطبيقات متجاهَلة.",
        ["Update checks, downloads and errors."] = "فحوصات التحديث والتنزيلات والأخطاء.",
        ["{0} apps, {1} with updates"] = "{0} تطبيقات، {1} بها تحديثات",
        ["{0} apps, all up to date"] = "{0} تطبيقات، كلها محدّثة",
        ["Latest release is more than {} days old"] = "مضى على أحدث إصدار أكثر من {} يومًا",
        ["Not updated in over {} days"] = "لم يتغيّر الإصدار منذ أكثر من {} يومًا",
        ["Source is archived"] = "المصدر مؤرشف",
        ["No logs in this period."] = "لا توجد سجلات في هذه الفترة.",
        ["Categories are created when you categorize apps. Deleting one removes it from every app."] = "تُنشأ الفئات عند تصنيف التطبيقات. حذف فئة يزيلها من كل التطبيقات.",
        ["Obtainium-PC - a native Windows port of Obtainium by ImranR98, licensed GPL-3.0. Brought to you by Instinct."] = "Obtainium-PC - منفذ أصلي لنظام ويندوز لتطبيق Obtainium من ImranR98، برخصة GPL-3.0. بتقديم Instinct.",
        ["One app source URL per line. Each is checked and added; failures are reported per URL."] = "رابط مصدر تطبيق واحد في كل سطر. يُفحص كل رابط ويُضاف؛ وتُعرض الأخطاء لكل رابط.",
        ["Options apply to this app only and are saved immediately, like the original."] = "تنطبق الخيارات على هذا التطبيق فقط وتُحفظ فورًا، كما في الأصل.",
        ["Paste the source URL of the app you want to track. GitHub, GitLab, Codeberg, and SourceForge are detected automatically; pick an override source (for example HTML) for anything else."] = "الصق رابط مصدر التطبيق الذي تريد تتبّعه. تُكتشف GitHub وGitLab وCodeberg وSourceForge تلقائيًا؛ اختر مصدرًا للتجاوز (مثل HTML) لأي مصدر آخر.",
        ["Reads an Obtainium export file (schema v2 and legacy formats). Apps with an existing ID are replaced; new apps are added."] = "يقرأ ملف تصدير Obtainium (المخطط v2 والصيغ القديمة). التطبيقات ذات المعرف الموجود تُستبدل، والجديدة تُضاف.",
        ["Search sources (GitHub, GitLab, Codeberg, F-Droid, Uptodown, RuStore, Huawei AppGallery)"] = "مصادر البحث (GitHub، GitLab، Codeberg، F-Droid، Uptodown، RuStore، Huawei AppGallery)",
        ["The source reports this repository moved. Review the new URL, then update or dismiss."] = "يفيد المصدر بأن هذا المستودع قد انتقل. راجع الرابط الجديد، ثم حدّث أو تجاهل.",
        ["Writes a file in the original Obtainium export format, so it can be imported by Obtainium on Android or by this app."] = "يكتب ملفًا بصيغة تصدير Obtainium الأصلية، ليمكن استيراده في Obtainium على أندرويد أو في هذا التطبيق.",
        ["Import apps in bulk from an external list. You review and pick which repos to add before anything is saved."] = "استيراد تطبيقات دفعة واحدة من قائمة خارجية. تراجع وتختار المستودعات قبل حفظ أي شيء.",
        ["More settings from the original (search, update health thresholds, logs, crowdsourced app configs) land as their pages are ported."] = "إعدادات أصلية إضافية تصل مع استكمال صفحاتها.",
        ["Repository renamed"] = "المستودع أُعيدت تسميته",

        // Tooltips
        ["Check for updates"] = "التحقق من التحديثات",
        ["Forget the recorded install so the app can be installed again"] = "نسيان التثبيت المسجّل ليتمكن التطبيق من إعادة التثبيت",
        ["Per-app options"] = "خيارات التطبيق",
        ["Windows app settings (packaged installs only)"] = "إعدادات التطبيق في ويندوز (للتثبيتات الحزمية فقط)",

        // Detail page and code strings
        ["Installed: "] = "المثبّت: ",
        ["Latest: "] = "الأحدث: ",
        ["Released: "] = "تاريخ الإصدار: ",
        ["not installed"] = "غير مثبّت",
        ["unknown"] = "غير معروف",
        ["Downloaded"] = "تم التنزيل",
        ["Download progress"] = "تقدّم التنزيل",
        ["Import complete"] = "اكتمل الاستيراد",
        ["{0} apps written to {1}."] = "تمت كتابة {0} من التطبيقات إلى {1}.",
        ["Added {0} apps. They resolve their first version on the next update check."] = "تمت إضافة {0} من التطبيقات. سيُحدَّد إصدارها الأول عند فحص التحديثات التالي.",
        ["Imported {0} apps."] = "تم استيراد {0} من التطبيقات.",
        ["Added: {0}; replaced: {1}."] = "أُضيف: {0}، واُستبدل: {1}.",
        ["Apps to import: {0}. Will replace existing entries: {1}."] = "التطبيقات المطلوب استيرادها: {0}. الإدخالات الموجودة التي ستُستبدل: {1}.",
        ["Import failed"] = "فشل الاستيراد",
        ["Close"] = "إغلاق",
        ["Import app"] = "استيراد تطبيق",
        ["Import apps"] = "استيراد تطبيقات",
        ["Raw JSON"] = "JSON خام",
        ["Invalid link"] = "رابط غير صالح",
        ["Invalid input"] = "إدخال غير صالح",
        ["Unknown link"] = "رابط غير معروف",
        ["Open Obtainium links (obtainium://) with Obtainium-PC"] = "فتح روابط Obtainium (obtainium://) بواسطة Obtainium-PC",
        ["Import from downloads"] = "استيراد من التنزيلات",
        ["Nothing to import"] = "لا يوجد ما يُستورَد",
        ["Clear logs?"] = "مسح السجلات؟",
        ["Delete all stored logs?"] = "حذف كل السجلات المخزّنة؟",
        ["Copied"] = "تم النسخ",
        ["Changes - "] = "التغييرات - ",
        ["Choose a file"] = "اختر ملفًا",
        ["Once the installer finishes, the installed version is detected automatically (registry, also on every update check). Use Detect installed version now to reconcile immediately; Mark as updated is the manual fallback when detection cannot find the app."] = "بعد انتهاء المثبّت، يُكتشف الإصدار المثبّت تلقائيًا (من السجل، وكذلك عند كل فحص تحديث). استخدم \"اكتشاف الإصدار المثبّت الآن\" للمطابقة الفورية؛ و\"تعليم كمُحدَّث\" هو البديل اليدوي عندما يتعذّر الاكتشاف.",
        ["Automatic installed-version detection is not available for this app. After installing, use Mark as updated."] = "الاكتشاف التلقائي للإصدار المثبّت غير متاح لهذا التطبيق. بعد التثبيت، استخدم \"تعليم كمُحدَّث\".",
        ["The app itself is not uninstalled; Obtainium-PC just stops tracking it."] = "التطبيق نفسه لا يُزال؛ يتوقف Obtainium-PC فقط عن تتبّعه.",
        ["Download failed: "] = "فشل التنزيل: ",
        ["Downloaded {0} and opened the installer."] = "تم تنزيل {0} وفتح المثبّت.",
        ["Downloaded {0}. Android package - cannot install on Windows; the file is saved for manual transfer."] = "تم تنزيل {0}. حزمة أندرويد - لا يمكن تثبيتها على ويندوز؛ حُفظ الملف للنقل اليدوي.",
        ["Remove {0}?"] = "إزالة {0}؟",
        ["Installed version detected: {0}"] = "تم اكتشاف الإصدار المثبّت: {0}",
        ["No installed version could be detected. If the installer has finished, try again in a moment; otherwise install the app first."] = "تعذّر اكتشاف إصدار مثبّت. إذا انتهى المثبّت، أعد المحاولة بعد قليل؛ وإلا فثبّت التطبيق أولًا.",
        ["Marked as updated to {0}."] = "عُلّم كمُحدَّث إلى {0}.",
        ["No downloadable asset on the latest release."] = "لا يوجد ملف قابل للتنزيل في أحدث إصدار.",
        ["Update check failed on the new URL: "] = "فشل فحص التحديث على الرابط الجديد: ",
        ["Changes"] = "التغييرات",
    };
}
