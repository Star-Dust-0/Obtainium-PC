using Microsoft.UI.Xaml.Controls;

namespace ObtainiumPC.Pages
{
    /// Placeholder shown in the detail pane of the Apps master/detail layout.
    public sealed partial class EmptyDetailPage : Page
    {
        public EmptyDetailPage()
        {
            this.InitializeComponent();
            Loaded += (_, _) => Services.Localization.Localizer.Apply(this);
        }
    }
}
