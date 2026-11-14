using System.Windows;
using System.Windows.Controls;
using Coclico.Services;
using Coclico.ViewModels;

namespace Coclico.Views;

public partial class CleaningView : UserControl
{
    internal CleaningViewModel ViewModel { get; }

    public CleaningView()
    {
        InitializeComponent();
        ViewModel = ServiceContainer.GetRequired<CleaningViewModel>();
        DataContext = ViewModel;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
        => AsyncEventSafe.Run(() => OnLoadedAsync(sender, e), "CleaningView.OnLoaded");

    private async Task OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        ViewModel.AttachLanguageChanged();
        await ViewModel.ScanAsync().ConfigureAwait(false);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.DetachLanguageChanged();
    }
}
