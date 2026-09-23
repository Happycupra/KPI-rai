using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class NewBatchWindow : Window
{
    private readonly ProductionOrderManagementViewModel plan;
    public int CreatedId { get; private set; }
    public NewBatchWindow(int? articleId = null)
    {
        InitializeComponent();
        try
        {
            plan = new ProductionOrderManagementViewModel(loadExistingOrders: false);
            DataContext = plan;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Planungsdaten für die neue Charge konnten nicht geladen werden. " + ex.Message, ex);
        }
        using var db = new AppDbContext();
        var routings = db.ManufacturingRoutings.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToList();
        routings.Insert(0, new ManufacturingRouting { Id = 0, Name = "Artikelvorgabe / ohne Arbeitsplan" });
        Routing.ItemsSource = routings;
        var articles = ArticleService.Search(activeOnly: true);
        Article.ItemsSource = articles;
        Article.SelectedItem = articleId.HasValue ? articles.FirstOrDefault(x => x.Id == articleId.Value) : articles.FirstOrDefault();
        if (articleId.HasValue && Article.SelectedItem is null) Message.Text = "Der ausgewählte Artikel ist nicht aktiv. Bitte einen aktiven Artikel auswählen.";
        if (articles.Count == 0) Message.Text = "Bitte zuerst unter Artikel einen aktiven Artikel anlegen.";
    }
    private void Article_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Article.SelectedItem is not ArticleMaster article) return;
        plan.Quantity = article.DefaultQuantity ?? 1;
        plan.Unit = article.Unit;
        Routing.SelectedValue = article.DefaultRoutingId ?? 0;
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Validation.GetHasError(QuantityInput) || Validation.GetHasError(ShiftCountInput) || Validation.GetHasError(StaffInput) || Validation.GetHasError(PlanDate) || !PlanDate.SelectedDate.HasValue)
                throw new InvalidOperationException("Bitte gültige Mengen, Schichten, Personal und ein Datum eingeben.");
            if (Article.SelectedItem is not ArticleMaster article || plan.SelectedWorkstation is null || plan.SelectedShift is null)
                throw new InvalidOperationException("Artikel, Arbeitsplatz und eine freigegebene Startschicht auswählen.");
            CreatedId = BatchService.CreateFromArticle(new(article.Id, plan.OrderNumber, plan.BatchNumber, plan.Quantity,
                plan.PlannedDate, plan.SelectedWorkstation.Id, plan.SelectedShift.Id, plan.PlannedShiftCount, plan.RequiredStaff,
                Routing.SelectedValue is int id && id > 0 ? id : null));
            DialogResult = true;
        }
        catch (Exception ex) { Message.Text = ex.Message; }
    }
}
