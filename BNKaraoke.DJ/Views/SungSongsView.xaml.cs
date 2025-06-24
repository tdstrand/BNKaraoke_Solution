using Serilog;
using System;
using System.Windows;

namespace BNKaraoke.DJ.Views
{
    public partial class SungSongsView : Window
    {
        public SungSongsView()
        {
            InitializeComponent();
            Log.Information("[SUNGSONGSVIEW] Initialized SungSongsView");
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            if (DataContext is BNKaraoke.DJ.ViewModels.SungSongsViewModel)
            {
                Log.Information("[SUNGSONGSVIEW] DataContext set to SungSongsViewModel");
            }
            else
            {
                Log.Warning("[SUNGSONGSVIEW] DataContext is not SungSongsViewModel: {DataContextType}", DataContext?.GetType().Name ?? "null");
            }
        }
    }
}