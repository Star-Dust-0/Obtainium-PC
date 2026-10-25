using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace ObtainiumPC.Services.Localization;

/// <summary>
/// Translates the static strings already present in a loaded visual tree
/// (English literals are the translation keys) and applies the flow
/// direction for RTL languages. Originals are remembered per element, so
/// re-applying after a language switch is idempotent and reversible.
/// Dynamic strings are localized at creation with Loc.T in code-behind.
/// </summary>
public static class Localizer
{
    private sealed class Original { public string Text = ""; public string Lang = "en"; }
    private static readonly ConditionalWeakTable<DependencyObject, Original> Originals = new();

    public static void Apply(DependencyObject root)
    {
        if (root is FrameworkElement fe)
            fe.FlowDirection = Loc.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Walk(root);
    }

    /// <summary>
    /// Translates NavigationViewItem objects held in a NavigationView's
    /// MenuItems/FooterMenuItems collections directly. The item containers
    /// leave the visual tree when the pane is unrealized or re-templating
    /// (e.g. right after a FlowDirection flip), so the tree walk alone can
    /// miss them and leave stale labels after a language switch.
    /// </summary>
    // Tag-keyed nav originals. WinUI can swap item container instances (a
    // fresh container inherits the already-translated text, so a per-instance
    // original is never recorded and the label gets stuck - ui-smoke scenario
    // 17 evidence, run 40214469046 logs). The Tag is stable across container
    // replacement, so the original survives.
    private static readonly System.Collections.Generic.Dictionary<string, string> NavOriginals = new();

    public static void ApplyToItems(System.Collections.Generic.IList<object> items)
    {
        foreach (var item in items)
            if (item is NavigationViewItem nvi)
                TranslateNavItem(nvi);
    }

    private static void TranslateNavItem(NavigationViewItem nvi)
    {
        if (nvi.Content is not string s) return;
        if (nvi.Tag is not string tag) { Translate(nvi); return; }
        if (!NavOriginals.TryGetValue(tag, out var key))
        {
            if (IsKey(s)) key = s;
            else if (Loc.TryReverse(s, out var reversed)) key = reversed;
            else return;
            NavOriginals[tag] = key;
        }
        var after = Loc.T(key);
        Logging.AppLogger.Info($"nav-item tag={tag} id={System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(nvi)} key=[{key}] [{s}] -> [{after}] lang={Loc.ResolvedLanguage}");
        nvi.Content = after;
    }

    /// Translates every tagged NavigationViewItem in any subtree. The overlay
    /// nav pane hosts cloned containers that keep the text they were created
    /// with (run 40216021873: MenuItems provably English while the open pane
    /// still rendered Arabic), so MenuItems translation alone cannot reach
    /// them; the tag map translates the clones too.
    public static void ApplyToNavContainers(DependencyObject root)
    {
        if (root is NavigationViewItem nvi) TranslateNavItem(nvi);
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
            ApplyToNavContainers(VisualTreeHelper.GetChild(root, i));
    }

    /// The compact-overlay pane lives in a Popup - a separate visual tree the
    /// normal walk cannot reach. Called after a language switch and on every
    /// PaneOpened so the cached cloned containers pick up the new language.
    public static void TranslatePopupNavItems(XamlRoot xamlRoot)
    {
        try
        {
            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot))
                if (popup.Child is DependencyObject child)
                    ApplyToNavContainers(child);
        }
        catch (System.Exception ex)
        {
            Logging.AppLogger.Warn("popup nav-item translation failed", ex);
        }
    }

    private static void Walk(DependencyObject parent)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            Translate(child);
            Walk(child);
        }
    }

    private static bool IsKey(string text) => Loc.T(text) != text;

    private static string FromOriginal(DependencyObject el, string current)
    {
        var holder = Originals.GetOrCreateValue(el);
        if (holder.Text.Length == 0)
        {
            // First visit: only track strings that are known keys, so
            // dynamic texts (versions, counts) are never frozen.
            if (!IsKey(current)) return current;
            holder.Text = current;
            holder.Lang = Loc.ResolvedLanguage;
        }
        else if (current == Loc.TranslateAs(holder.Text, holder.Lang))
        {
            // Element shows the original or its translation under the
            // language that was active at the last visit - re-translate
            // under the current language (this is what makes switching
            // back from Arabic to English work: the Arabic text is not a
            // key, so without the remembered language it looked dynamic).
            holder.Lang = Loc.ResolvedLanguage;
        }
        else if (IsKey(current))
        {
            // Dynamic label moved to another known key (e.g. Enable/Disable).
            holder.Text = current;
            holder.Lang = Loc.ResolvedLanguage;
        }
        else
        {
            // Code set a new dynamic string that is not a key: leave it.
            return current;
        }
        return Loc.T(holder.Text);
    }

    private static void Translate(DependencyObject el)
    {
        switch (el)
        {
            case TextBlock tb when !string.IsNullOrEmpty(tb.Text):
                tb.Text = FromOriginal(tb, tb.Text);
                break;
            case Button { Content: string } btn:
                btn.Content = FromOriginal(btn, (string)btn.Content);
                break;
            // RadioButton inherits ToggleButton; both land here.
            case ToggleButton { Content: string } tg:
                tg.Content = FromOriginal(tg, (string)tg.Content);
                break;
            case ToggleSwitch ts:
                if (ts.Header is string tsh) ts.Header = FromOriginal(ts, tsh);
                break;
            case ComboBox cb:
                if (cb.Header is string cbh) cb.Header = FromOriginal(cb, cbh);
                break;
            case ComboBoxItem { Content: string } cbi:
                cbi.Content = FromOriginal(cbi, (string)cbi.Content);
                break;
            case AutoSuggestBox asb when !string.IsNullOrEmpty(asb.PlaceholderText):
                asb.PlaceholderText = FromOriginal(asb, asb.PlaceholderText);
                break;
            case TextBox tbx:
                if (tbx.Header is string tbxh) tbx.Header = FromOriginal(tbx, tbxh);
                break;
            case PasswordBox pb:
                if (pb.Header is string pbh) pb.Header = FromOriginal(pb, pbh);
                break;
            case InfoBar ib:
                if (ib.Title is string ibt && !string.IsNullOrEmpty(ibt)) ib.Title = FromOriginal(ib, ibt);
                if (ib.Message is string ibm && !string.IsNullOrEmpty(ibm)) ib.Message = FromOriginal(ib, ibm);
                break;
            case NavigationViewItem { Content: string } nvi:
            {
                var before = (string)nvi.Content;
                var after = FromOriginal(nvi, before);
                Logging.AppLogger.Info($"walk-nvi tag={nvi.Tag} [{before}] -> [{after}] lang={Loc.ResolvedLanguage}");
                nvi.Content = after;
                break;
            }
            case HyperlinkButton { Content: string } hb:
                hb.Content = FromOriginal(hb, (string)hb.Content);
                break;
        }
    }
}
