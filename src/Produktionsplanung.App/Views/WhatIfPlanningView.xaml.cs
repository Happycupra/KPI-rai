using System.Windows.Controls;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class WhatIfPlanningView : UserControl
{
    public WhatIfPlanningView()
    {
        InitializeComponent();
        DataContext = new WhatIfPlanningViewModel();
    }
}
