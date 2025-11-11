using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;

namespace BNKaraoke.DJ.Views
{
    public partial class DJScreen : UserControl
    {
        private DJScreenViewModel ViewModel => (DJScreenViewModel)DataContext;

        public DJScreen()
        {
            InitializeComponent();
            DataContextChanged += DJScreen_DataContextChanged;
            Loaded += DJScreen_Loaded;
        }

        private void DJScreen_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is DJScreenViewModel oldVm)
            {
                oldVm.PropertyChanged -= ViewModel_PropertyChanged;
            }
            if (e.NewValue is DJScreenViewModel newVm)
            {
                newVm.PropertyChanged += ViewModel_PropertyChanged;
                UpdateBinding();
            }
        }

        private void DJScreen_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateBinding();
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DJScreenViewModel.QueueEntriesInternal))
            {
                UpdateBinding();
            }
        }

        private void UpdateBinding()
        {
            if (ViewModel != null && QueueItemsListView != null)
            {
                QueueItemsListView.ItemsSource = ViewModel.QueueEntriesInternal;
                Serilog.Log.Information("[DJSCREEN] DYNAMIC BINDING: QueueItemsListView bound to QueueEntriesInternal ({Count} items)", ViewModel.QueueEntriesInternal.Count);
            }
        }
    }
}
