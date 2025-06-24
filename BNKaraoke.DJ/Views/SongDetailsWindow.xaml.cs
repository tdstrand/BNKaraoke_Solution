using BNKaraoke.DJ.ViewModels;
using Serilog; // Added for Log
using System;
using System.Windows;

namespace BNKaraoke.DJ.Views
{
    public partial class SongDetailsWindow : Window
    {
        public SongDetailsWindow()
        {
            InitializeComponent();
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            if (DataContext is SongDetailsViewModel)
            {
                Log.Information("[SONGDETAILSWINDOW] DataContext set to SongDetailsViewModel");
            }
            else
            {
                Log.Warning("[SONGDETAILSWINDOW] DataContext is not SongDetailsViewModel: {DataContextType}", DataContext?.GetType().Name ?? "null");
            }
        }
    }
}