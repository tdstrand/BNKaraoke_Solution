using System.Windows;
using BNKaraoke.DJ.ViewModels;

namespace BNKaraoke.DJ.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        DataContext = new SettingsWindowViewModel();
    }
}