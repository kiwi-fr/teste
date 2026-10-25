using System.Windows.Controls;
using Coclico.Modules.UpdateCenter.ViewModels;

namespace Coclico.Modules.UpdateCenter.Views;

public partial class UpdateCenterView : UserControl
{
    public UpdateCenterView(UpdateCenterViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Unloaded += (_, _) => viewModel.Dispose();
    }
}