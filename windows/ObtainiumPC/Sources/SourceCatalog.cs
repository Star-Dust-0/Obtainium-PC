using ObtainiumPC.Services.Settings;
using ObtainiumPC.Sources.Apk4Free;
using ObtainiumPC.Sources.ApkPure;
using ObtainiumPC.Sources.ApkCombo;
using ObtainiumPC.Sources.Aptoide;
using ObtainiumPC.Sources.GitHub;
using ObtainiumPC.Sources.Codeberg;
using ObtainiumPC.Sources.CoolApk;
using ObtainiumPC.Sources.DirectLink;
using ObtainiumPC.Sources.Farsroid;
using ObtainiumPC.Sources.FDroid;
using ObtainiumPC.Sources.GitLab;
using ObtainiumPC.Sources.HuaweiAppGallery;
using ObtainiumPC.Sources.ItchIo;
using ObtainiumPC.Sources.Jenkins;
using ObtainiumPC.Sources.LiteApks;
using ObtainiumPC.Sources.NeutronCode;
using ObtainiumPC.Sources.RockMods;
using ObtainiumPC.Sources.RuStore;
using ObtainiumPC.Sources.SourceHut;
using ObtainiumPC.Sources.TelegramApp;
using ObtainiumPC.Sources.Tencent;
using ObtainiumPC.Sources.Uptodown;
using ObtainiumPC.Sources.VivoAppStore;
using ObtainiumPC.Sources.SourceForge;

namespace ObtainiumPC.Sources;

/// <summary>
/// Resolves a URL to its app source. Shared by the update service and the UI so
/// source detection logic lives in exactly one place.
/// </summary>
public static class SourceCatalog
{
    /// All sources that can be picked as an explicit override (upstream's
    /// "Override Source" dropdown). HTML has no hosts of its own.
    public static IReadOnlyList<string> OverrideSourceNames => new[] { "GitHub", "GitLab", "Forgejo (Codeberg)", "SourceForge", "SourceHut", "NeutronCode", "itch.io", "F-Droid", "F-Droid Third-Party Repo", "Jenkins", "Telegram App", "IzzyOnDroid", "Tencent App Store", "Aptoide", "RockMods", "Farsroid", "Vivo App Store", "LiteAPKs", "Apk4Free", "APKCombo", "APKPure", "Uptodown", "RuStore", "Huawei AppGallery", "HTML", "Direct File Link" };

    public static AppSourceBase ResolveByName(string name, AppSettings settings) => name switch
    {
        "GitHub" => Resolve("https://github.com/x/y", settings),
        "GitLab" => Resolve("https://gitlab.com/x/y", settings),
        "Forgejo (Codeberg)" => Resolve("https://codeberg.org/x/y", settings),
        "SourceForge" => new SourceForge.SourceForgeSource(),
        "SourceHut" => new SourceHutSource(),
        "NeutronCode" => new NeutronCodeSource(),
        "itch.io" => new ItchIoSource(),
        "F-Droid" => new FDroidSource(),
        "F-Droid Third-Party Repo" => new FDroidRepoSource(),
        "Jenkins" => new JenkinsSource(),
        "Telegram App" => new TelegramAppSource(),
        "IzzyOnDroid" => new IzzyOnDroidSource(),
        "Tencent App Store" => new TencentSource(),
        "Aptoide" => new AptoideSource(),
        "RockMods" => new RockModsSource(),
        "Farsroid" => new FarsroidSource(),
        "Vivo App Store" => new VivoAppStoreSource(),
        "LiteAPKs" => new LiteApksSource(),
        "Apk4Free" => new Apk4FreeSource(),
        "APKCombo" => new ApkComboSource(),
        "APKPure" => new ApkPureSource(),
        "Uptodown" => new UptodownSource(),
        "RuStore" => new RuStoreSource(),
        "Huawei AppGallery" => new HuaweiAppGallerySource(),
        "HTML" => new Html.HtmlSource(),
        "Direct File Link" => new DirectFileLinkSource(),
        _ => throw new InvalidUrlException(name),
    };

    public static AppSourceBase Resolve(string url, AppSettings settings, string? overrideSource = null)
    {
        if (!string.IsNullOrEmpty(overrideSource)) return ResolveByName(overrideSource, settings);
        var host = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : "";
        if (host == "github.com" || host == "www.github.com")
            return new GitHubSource(new GitHubSourceConfig(
                PersonalAccessToken: settings.GitHubPersonalAccessToken,
                GhReqPrefix: settings.GitHubReqPrefix,
                CheckRepoRename: settings.GitHubCheckRepoRename,
                GlobalMinimumUpdateAgeDays: settings.MinimumUpdateAgeDays));
        if (host == "gitlab.com" || host == "www.gitlab.com")
            return new GitLabSource(new GitLabSourceConfig(
                PersonalAccessToken: settings.GitLabPersonalAccessToken,
                GlobalMinimumUpdateAgeDays: settings.MinimumUpdateAgeDays));
        if (host == "codeberg.org" || host == "www.codeberg.org")
            return new CodebergSource(new CodebergSourceConfig(
                ForgejoPersonalAccessToken: settings.ForgejoPersonalAccessToken,
                GlobalMinimumUpdateAgeDays: settings.MinimumUpdateAgeDays));
        if (host == "apkcombo.com" || host == "www.apkcombo.com")
            return new ApkComboSource();
        if (host == "apkpure.net" || host == "apkpure.com" || host.EndsWith(".apkpure.net") || host.EndsWith(".apkpure.com"))
            return new ApkPureSource();
        if (host == "uptodown.com" || host.EndsWith(".uptodown.com"))
            return new UptodownSource();
        if (host == "rustore.ru" || host.EndsWith(".rustore.ru"))
            return new RuStoreSource();
        if (host == "appgallery.huawei.com" || host == "appgallery.cloud.huawei.com" || host == "appgallery.huawei.ru"
            || host.EndsWith(".appgallery.huawei.com") || host.EndsWith(".appgallery.cloud.huawei.com") || host.EndsWith(".appgallery.huawei.ru"))
            return new HuaweiAppGallerySource();
        if (host == "apk4free.net" || host == "www.apk4free.net")
            return new Apk4FreeSource();
        if (host == "liteapks.com" || host == "www.liteapks.com")
            return new LiteApksSource();
        if (host == "farsroid.com" || host == "www.farsroid.com")
            return new FarsroidSource();
        if (host == "h5.appstore.vivo.com.cn" || host == "h5coml.vivo.com.cn" || host == "detail-browser.vivo.com.cn")
            return new VivoAppStoreSource();
        if (host == "aptoide.com" || host.EndsWith(".aptoide.com"))
            return new AptoideSource();
        if (host == "rockmods.net" || host == "www.rockmods.net")
            return new RockModsSource();
        if (host == "sj.qq.com")
            return new TencentSource();
        if (host == "coolapk.com" || host.EndsWith(".coolapk.com"))
            return new CoolApkSource();
        if (host == "android.izzysoft.de" || host == "apt.izzysoft.de" || host == "izzysoft.de" || host == "www.izzysoft.de")
            return new IzzyOnDroidSource();
        if (host == "telegram.org" || host == "www.telegram.org")
            return new TelegramAppSource();
        if (host == "f-droid.org" || host == "www.f-droid.org")
            return new FDroidSource();
        if (host == "itch.io" || host.EndsWith(".itch.io"))
            return new ItchIoSource();
        if (host == "neutroncode.com" || host == "www.neutroncode.com")
            return new NeutronCodeSource();
        if (host == "git.sr.ht")
            return new SourceHutSource();
        if (host == "sourceforge.net" || host == "www.sourceforge.net")
            return new SourceForgeSource();
        throw new InvalidUrlException(host.Length > 0 ? host : url);
    }

    public static bool TryResolve(string url, AppSettings settings, out AppSourceBase? source, string? overrideSource = null)
    {
        try { source = Resolve(url, settings, overrideSource); return true; }
        catch (ObtainiumSourceException) { source = null; return false; }
    }
}
