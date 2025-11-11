using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using BNKaraoke.DJ.ViewModels;
using BNKaraoke.DJ.Services;
using Serilog;

namespace BNKaraoke.DJ.Views
{
    /// <summary>
    /// Interaction logic for DJScreen.xaml
    /// </summary>
    public partial class DJScreen
    {
        private DJScreenViewModel ViewModel => (DJScreenViewModel)DataContext;

        public DJScreen()
        {
            InitializeComponent();
            DataContextChanged += DJScreen_DataContextChanged;
            Loaded += DJScreen_Loaded;

            if (DataContext == null)
            {
                try
                {
                    DataContext = new DJScreenViewModel();
                }
                catch (Exception ex)
                {
                    Log.Error("[DJSCREEN] Failed to initialize DJScreen: {Message}", ex.Message);
                    MessageBox.Show($"Failed to initialize DJScreen: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Close();
                }
            }
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

            try
            {
                if (DataContext is not DJScreenViewModel)
                {
                    Log.Error("[DJSCREEN] Failed to load ViewModel");
                    MessageBox.Show("Failed to load ViewModel.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Close();
                    return;
                }

                var settings = SettingsService.Instance.Settings;
                if (settings.MaximizedOnStart)
                {
                    WindowState = WindowState.Maximized;
                }
                else
                {
                    var workArea = SystemParameters.WorkArea;
                    Top = workArea.Top;
                    Left = workArea.Left;
                    Width = workArea.Width;
                    Height = workArea.Height;
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to load DJScreen: {Message}", ex.Message);
                MessageBox.Show($"Failed to load DJScreen: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DJScreenViewModel.QueueEntriesInternal))
            {
                UpdateBinding();
            }
        }

        private void Slider_DragStarted(object sender, DragStartedEventArgs e) { }

        private void Slider_DragCompleted(object sender, DragCompletedEventArgs e) { }

        private void Slider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) { }

        private void Slider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) { }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { }

        private void ListViewItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListViewItem item && !item.IsSelected)
            {
                item.IsSelected = true;
            }
        }

        private void ListViewItem_PreviewMouse(object sender, MouseEventArgs e) { }

        private void QueueListView_ContextMenuOpening(object sender, ContextMenuEventArgs e) { }

        private void SingersContextMenu_Opening(object sender, ContextMenuEventArgs e) { }

        private void QueueListView_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e) { }

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
