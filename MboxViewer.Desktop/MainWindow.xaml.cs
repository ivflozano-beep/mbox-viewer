using System.Windows;
using MboxViewer.Desktop.ViewModels;

namespace MboxViewer.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}