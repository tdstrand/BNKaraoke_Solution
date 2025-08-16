using BNKaraoke.DJ.ViewModels;
using Serilog;
using System;
using System.Windows;

namespace BNKaraoke.DJ.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsWindowViewModel _viewModel;

        public SettingsWindow()
        {
            InitializeComponent();
            _viewModel = new SettingsWindowViewModel();
            DataContext = _viewModel;
        }

        protected override void OnClosed(EventArgs e)
        {
            Log.Information("[SETTINGS WINDOW] SettingsWindow closed");
            base.OnClosed(e);
        }
    }
}