using System.Windows.Controls;
using Coclico.Modules.TelemetryBlocker.Services;
using Coclico.Modules.TelemetryBlocker.ViewModels;
using Coclico.Services;

namespace Coclico.Modules.TelemetryBlocker.Views;

public partial class TelemetryBlockerView : UserControl
{
    public TelemetryBlockerView()
    {
        InitializeComponent();
        var vm = ServiceContainer.GetOptional<TelemetryBlockerViewModel>()
                 ?? new TelemetryBlockerViewModel(
                     ServiceContainer.GetRequired<TelemetryBlockService>(),
                     ServiceContainer.GetRequired<SettingsService>());
        DataContext = vm;
    }
}
