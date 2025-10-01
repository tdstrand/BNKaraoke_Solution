using BNKaraoke.DJ.ViewModels;
using System.Windows;

namespace BNKaraoke.DJ.Views
{
    public partial class ReorderQueueModal : Window
    {
        public ReorderQueueModal()
        {
            InitializeComponent();
        }

        private void Window_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is ReorderQueueModalViewModel oldViewModel)
            {
                oldViewModel.RequestClose -= OnRequestClose;
            }

            if (e.NewValue is ReorderQueueModalViewModel newViewModel)
            {
                newViewModel.RequestClose += OnRequestClose;
            }
        }

        private void OnRequestClose(object? sender, bool approved)
        {
            DialogResult = approved;
            Close();
        }
    }
}
