using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Produktionsplanung.App.Data;
namespace Produktionsplanung.App.Services;

public static class CloudEditBaseline
{
    private static readonly Dictionary<Window,Dictionary<string,long>> Baselines=new();
    public static bool HasDrafts => Baselines.Count>0;
    public static void Attach(Window window)
    {
        if(!FirebaseCompanyClient.Enabled)return;
        void Capture(object? source)
        {
            if(source is not DependencyObject d || !window.IsLoaded)return;
            DependencyObject? current=d;
            for(var i=0;i<8 && current is not null;i++,current=Parent(current))
                if(current is TextBoxBase or ComboBox or DatePicker or CheckBox) { Begin(window); break; }
        }
        window.PreviewKeyDown+=(_,e)=>Capture(e.OriginalSource);window.PreviewMouseDown+=(_,e)=>Capture(e.OriginalSource);
        window.Closed+=(_,_)=>Baselines.Remove(window);
    }
    private static DependencyObject? Parent(DependencyObject value) => value is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
        ? System.Windows.Media.VisualTreeHelper.GetParent(value) : value is FrameworkContentElement element ? element.Parent : LogicalTreeHelper.GetParent(value);
    private static Window? Active => Application.Current?.Dispatcher.CheckAccess()==true ? Application.Current.Windows.Cast<Window>().FirstOrDefault(w=>w.IsActive):null;
    private static void Begin(Window window)
    {
        if(Baselines.ContainsKey(window))return;
        using var db=new AppDbContext();FirebaseCloudStorage.EnsureTables(db);
        Baselines[window]=FirebaseCloudStorage.Query(db,"SELECT Kind,LocalKey,Version FROM CloudRowIds").ToDictionary(r=>(string)r[0]+":"+(string)r[1],r=>Convert.ToInt64(r[2]));
    }
    internal static long ExpectedVersion(string kind,string key,long fallback)=>Active is {} window && Baselines.TryGetValue(window,out var rows)&&rows.TryGetValue(kind+":"+key,out var version)?version:fallback;
    internal static void Saved() { if(Active is {} window)Baselines.Remove(window); }
    public static void Clear()=>Baselines.Clear();
}
