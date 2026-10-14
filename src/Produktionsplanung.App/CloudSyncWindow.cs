using System.Windows;
using System.Windows.Controls;
using System.Text.Json;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Services;
namespace Produktionsplanung.App;

public sealed class CloudSyncWindow : Window
{
    private sealed record QueueRow(long Id,string Summary,string Error,string Payload,bool Conflict);
    private readonly ListBox queue=new(){MinHeight=140};private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};
    public CloudSyncWindow()
    {
        Title="Firmenkonto · Synchronisation";Width=760;Height=560;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel{Margin=new Thickness(24)};Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        panel.Children.Add(new TextBlock{Text="Synchronisation mit Firebase",FontSize=22,FontWeight=FontWeights.SemiBold});panel.Children.Add(status);panel.Children.Add(queue);queue.DisplayMemberPath=nameof(QueueRow.Summary);
        queue.SelectionChanged+=(_,_)=>status.Text=(queue.SelectedItem as QueueRow)?.Error??FirebaseCompanySync.Status;
        var buttons=new WrapPanel{Margin=new Thickness(0,12,0,0)};panel.Children.Add(buttons);
        Add(buttons,"Erneut synchronisieren",Retry);Add(buttons,"Konflikt vergleichen",Compare);Add(buttons,"Firmendaten neu laden",Discard);
        panel.Children.Add(new TextBlock{Text="Nicht übertragene Änderungen bleiben auf diesem PC erhalten. Konflikte werden erst nach deiner Entscheidung aufgelöst.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)});
        Loaded+=(_,_)=>Refresh();
    }
    private static void Add(Panel panel,string text,RoutedEventHandler action){var button=new Button{Content=text,Margin=new Thickness(0,0,8,8),Padding=new Thickness(10,7,10,7)};button.Click+=action;panel.Children.Add(button);}
    private void Refresh()
    {
        using var db=new AppDbContext();FirebaseCloudStorage.EnsureTables(db);
        queue.ItemsSource=FirebaseCloudStorage.Query(db,"SELECT Id,Payload,LastError,NextRetryAtUtc FROM CloudOutbox ORDER BY Id").Select(r=>new QueueRow(Convert.ToInt64(r[0]),$"Änderungspaket {r[0]} · {((string)r[3]=="9999-12-31"?"Prüfung erforderlich":"ausstehend")}",(string)r[2],(string)r[1],(string)r[3]=="9999-12-31")).ToList();status.Text=FirebaseCompanySync.Status;
    }
    private async void Retry(object sender,RoutedEventArgs e)
    {
        try { using(var db=new AppDbContext())FirebaseCloudStorage.Sql(db,"UPDATE CloudOutbox SET NextRetryAtUtc='' WHERE NextRetryAtUtc<>'9999-12-31'");await FirebaseCompanySync.SynchronizeAsync();Refresh(); }catch(Exception ex){status.Text=ex.Message;}
    }
    private async void Discard(object sender,RoutedEventArgs e)
    {
        if(MessageBox.Show("Die gemeinsamen Firmendaten neu laden und alle noch nicht übertragenen Änderungen dieses PCs verwerfen? Die bisherige Arbeitskopie wird zusätzlich gesichert.",Title,MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        await FirebaseCompanySync.PauseAsync();
        try { CloudEditBaseline.Clear();await FirebaseCompanySync.BootstrapAsync(null);Refresh(); }
        catch(Exception ex){status.Text=ex.Message;}
        finally{FirebaseCompanySync.Start();}
    }
    private async void Compare(object sender,RoutedEventArgs e)
    {
        if(queue.SelectedItem is not QueueRow selected||!selected.Conflict){status.Text="Bitte zuerst ein Änderungspaket mit Konflikt auswählen. Bei Verbindungsfehlern genügt „Erneut synchronisieren“.";return;}
        try
        {
            var local=JsonSerializer.Deserialize<List<FirebaseCloudRecord>>(selected.Payload,FirebaseCompanyClient.Json)!;
            var remote=await FirebaseCompanyClient.CallAsync<CloudApplyResult>("cloudGetRecords",new{keys=local.Select(r=>new{r.Kind,r.Id}).ToArray()});
            var rows=new List<FieldComparison>();
            foreach(var r in local)
            {
                var current=remote.Records.Single(s=>s.Kind==r.Kind&&s.Id==r.Id);
                var title=FirebaseCloudStorage.DisplayTitle(r.Kind,r.Data);
                rows.Add(new(title,"Datensatz",r.Deleted?"Entfernen":"Bearbeitet",current.Deleted?"Nicht vorhanden":"Vorhanden"));
                using var displayDb=new AppDbContext();
                foreach(var field in r.Data)if(field.Value?.ToJsonString()!=current.Data[field.Key]?.ToJsonString())rows.Add(new(title,Label(field.Key),FirebaseCloudStorage.DisplayField(displayDb,r.Kind,field.Key,field.Value),FirebaseCloudStorage.DisplayField(displayDb,r.Kind,field.Key,current.Data[field.Key])));
            }
            var dialog=new Window{Title="Konflikt vergleichen",Width=900,Height=600,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner};
            var panel=new DockPanel{Margin=new Thickness(20)};dialog.Content=panel;
            var accept=new Button{Content="Meine angezeigten Änderungen erneut übernehmen",Padding=new Thickness(10),Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(accept,Dock.Bottom);panel.Children.Add(accept);
            var grid=new DataGrid{ItemsSource=rows,IsReadOnly=true,AutoGenerateColumns=true};panel.Children.Add(grid);var accepted=false;accept.Click+=(_,_)=>{accepted=true;dialog.Close();};dialog.ShowDialog();
            if(!accepted)return;
            using(var db=new AppDbContext())FirebaseCompanySync.RebaseConflict(db,selected.Id,remote.Records);
            await FirebaseCompanySync.SynchronizeAsync();Refresh();
        }
        catch(Exception ex){status.Text=ex.Message;}
    }
    private sealed record FieldComparison(string Bereich,string Feld,string Lokal,string Firma);
    private static string Label(string field)=>field switch {"FirstName"=>"Vorname","LastName"=>"Nachname","PersonnelNumber"=>"Personalnummer","Name"=>"Name","Subject"=>"Betreff","Details"=>"Beschreibung","Quantity"=>"Menge","Status"=>"Status","Priority"=>"Priorität","Comment"=>"Notiz",_=>System.Text.RegularExpressions.Regex.Replace(field,"([a-z])([A-Z])","$1 $2")};
}
