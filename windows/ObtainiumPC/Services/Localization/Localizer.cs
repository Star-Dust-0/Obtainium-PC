using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    private sealed class Original { public string Text = ""; }
    private static readonly ConditionalWeakTable<DependencyObject, Original> Originals = new();

    public static void Apply(DependencyObject root)
    {
        if (root is FrameworkElement fe)
            fe.FlowDirection = Loc.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Walk(root);
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
        }
        else if (current != holder.Text && IsKey(current))
        {
            // Dynamic label moved to another known key (e.g. Enable/Disable).
            holder.Text = current;
        }
        else if (current != holder.Text && current != Loc.T(holder.Text))
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
            case NavigationViewItem { Content: string } nvi:
                nvi.Content = FromOriginal(nvi, (string)nvi.Content);
                break;
            case HyperlinkButton { Content: string } hb:
                hb.Content = FromOriginal(hb, (string)hb.Content);
                break;
            case RadioButton { Content: string } rb:
                rb.Content = FromOriginal(rb, (string)rb.Content);
                break;
        }
    }
}
