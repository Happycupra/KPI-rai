using System.Globalization;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.Views;

public partial class ArticleEditorWindow : Window
{
    private readonly ArticleMaster article;
    public int SavedId { get; private set; }
    public ArticleEditorWindow(int? articleId = null)
    {
        InitializeComponent();
        article = articleId.HasValue ? ArticleService.GetDetails(articleId.Value) : new ArticleMaster();
        Number.Text = article.ArticleNumber;
        ArticleName.Text = article.Name;
        Unit.ItemsSource = new[] { "Stück", "kg", "g", "l", "ml", "Charge" };
        Unit.Text = article.Unit;
        Quantity.Text = article.DefaultQuantity?.ToString(CultureInfo.CurrentCulture) ?? "";
        Rate.Text = article.DefaultIdealRatePerHour.ToString(CultureInfo.CurrentCulture);
        Active.IsChecked = article.IsActive;
        Notes.Text = article.Notes ?? "";
        using var db = new AppDbContext();
        var routings = db.ManufacturingRoutings.AsNoTracking().Where(x => x.IsActive || x.Id == article.DefaultRoutingId).OrderBy(x => x.Name).ToList();
        routings.Insert(0, new ManufacturingRouting { Id = 0, Name = "Kein Standard-Arbeitsplan" });
        Routing.ItemsSource = routings;
        Routing.SelectedValue = article.DefaultRoutingId ?? 0;
        Number.IsReadOnly = article.Id > 0 && db.ProductionOrders.Any(x => x.ArticleMasterId == article.Id || x.ArticleNumber == article.ArticleNumber);
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!double.TryParse(Rate.Text, out var rate) || !string.IsNullOrWhiteSpace(Quantity.Text) && !double.TryParse(Quantity.Text, out _))
                throw new InvalidOperationException("Bitte gültige Zahlen für Menge und Sollrate eingeben.");
            article.ArticleNumber = Number.Text;
            article.Name = ArticleName.Text;
            article.Unit = Unit.Text;
            article.DefaultQuantity = string.IsNullOrWhiteSpace(Quantity.Text) ? null : double.Parse(Quantity.Text);
            article.DefaultIdealRatePerHour = rate;
            article.DefaultRoutingId = Routing.SelectedValue is int id && id > 0 ? id : null;
            article.IsActive = Active.IsChecked == true;
            article.Notes = Notes.Text;
            SavedId = ArticleService.Save(article);
            DialogResult = true;
        }
        catch (Exception ex) { Message.Text = ex.Message; }
    }
}
