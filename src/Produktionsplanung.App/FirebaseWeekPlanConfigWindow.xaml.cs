using System.Windows;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App;

public partial class FirebaseWeekPlanConfigWindow : Window
{
    public FirebaseWeekPlanConfigWindow()
    {
        InitializeComponent();
        if (!SessionService.IsAdministrator)
        {
            MessageBox.Show("Nur Administratoren dürfen die Online-Wochenplan-Konfiguration ändern.",
                "Online-Wochenplan", MessageBoxButton.OK, MessageBoxImage.Warning);
            Loaded += (_, _) => Close();
            return;
        }

        var settings = AppSettingsService.Load();
        EnabledBox.IsChecked = settings.OnlineWeekPlanEnabled;
        ProjectIdBox.Text = settings.FirebaseProjectId;
        ApiKeyBox.Text = settings.FirebaseWebApiKey;
        AuthEndpointBox.Text = settings.FirebaseAuthEndpoint;
        HostingUrlBox.Text = settings.FirebaseHostingUrl;
        PublishEndpointBox.Text = settings.FirebasePublishEndpoint;
        StatusText.Text = OnlineWeekPlanService.FirebaseStatusText(settings);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var auth = AuthEndpointBox.Text.Trim();
        var hosting = HostingUrlBox.Text.Trim();
        var publish = PublishEndpointBox.Text.Trim();

        if (!ValidOptionalHttps(auth) || !ValidOptionalHttps(hosting) || !ValidOptionalHttps(publish))
        {
            StatusText.Text = "URLs müssen leer sein oder mit https:// beginnen.";
            return;
        }

        var settings = AppSettingsService.Update(value =>
        {
            value.OnlineWeekPlanEnabled = EnabledBox.IsChecked == true;
            value.FirebaseProjectId = ProjectIdBox.Text.Trim();
            value.FirebaseWebApiKey = ApiKeyBox.Text.Trim();
            value.FirebaseAuthEndpoint = auth;
            value.FirebaseHostingUrl = hosting;
            value.FirebasePublishEndpoint = publish;
        });

        StatusText.Text = OnlineWeekPlanService.FirebaseStatusText(settings);
        DialogResult = true;
    }

    private static bool ValidOptionalHttps(string value) =>
        string.IsNullOrWhiteSpace(value) ||
        (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
         string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
}
