using System.Linq;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ObtainiumPC.Pages
{
    public sealed partial class AppOptionsPage : Page
    {
        public AppOptionsPage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await App.InitAsync();
            var id = e.Parameter as string;
            var app = App.TrackedApps.FirstOrDefault(a => a.Id == id);
            if (app == null) { Frame.GoBack(); return; }
            TitleText.Text = app.FinalName + " options";
            RawSettings.Text = app.AdditionalSettingsJson;
        }
    }
}
