using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App;

public partial class UserQrCodeWindow : Window
{
    private readonly string companyName;
    private readonly string companyCode;
    private readonly string username;
    private readonly string displayName;
    private readonly string loginUrl;
    private readonly byte[] qrPng;

    public UserQrCodeWindow(string companyName, string companyCode, string username, string displayName)
    {
        InitializeComponent();

        this.companyName = companyName;
        this.companyCode = companyCode;
        this.username = username;
        this.displayName = string.IsNullOrWhiteSpace(displayName) ? username : displayName;

        loginUrl = UserLoginQrCodeService.BuildLoginUrl(companyCode, username);
        qrPng = UserLoginQrCodeService.CreatePng(loginUrl);

        QrImage.Source = ToBitmap(qrPng);
        DisplayNameText.Text = this.displayName;
        UsernameText.Text = $"Benutzername: {username}";
        CompanyText.Text = $"{companyName} · {companyCode}";
        LoginUrlBox.Text = loginUrl;
    }

    private static BitmapImage ToBitmap(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true)
            return;

        dialog.PrintVisual(QrPrintPanel, $"SolutionCompakt QR-Code · {username}");
    }

    private void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        var safeUser = string.Concat(username.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var dialog = new SaveFileDialog
        {
            Filter = "PDF-Datei (*.pdf)|*.pdf",
            FileName = $"SolutionCompakt-Login-QR-{safeUser}.pdf",
            AddExtension = true,
            DefaultExt = ".pdf"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        UserLoginQrCodeService.ExportPdf(
            dialog.FileName,
            companyName,
            companyCode,
            username,
            displayName,
            loginUrl,
            qrPng);

        MessageBox.Show(
            this,
            "Der Login-QR-Code wurde als PDF exportiert.",
            "SolutionCompakt",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
