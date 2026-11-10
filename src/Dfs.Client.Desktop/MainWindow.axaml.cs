using Avalonia.Controls;
using Dfs.Client.Desktop.ViewModels;

namespace Dfs.Client.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
