using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BNKaraoke.DJ.Services;
using BNKaraoke.DJ.ViewModels;
using Serilog;
using System.Windows.Controls.Primitives;

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
                var workArea = SystemParameters.WorkArea;

                if (settings.MaximizedOnStart)
                {
                    MaxHeight = workArea.Height;
                    MaxWidth = workArea.Width;
                    WindowState = WindowState.Maximized;
                }
                else
                {
                    WindowState = WindowState.Normal;
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

        private void ListViewItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListViewItem item && !item.IsSelected)
            {
                item.IsSelected = true;
            }
        }

        private void QueueListView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (sender is not ListView listView || DataContext is not DJScreenViewModel viewModel)
            {
                return;
            }

            var contextSource = e.OriginalSource as DependencyObject;
            var container = FindAncestor<ListViewItem>(contextSource);

            QueueEntryViewModel? entry = null;
            if (container?.DataContext is QueueEntryViewModel itemEntry)
            {
                if (!container.IsSelected)
                {
                    container.IsSelected = true;
                }
                entry = itemEntry;
            }
            else if (listView.SelectedItem is QueueEntryViewModel selectedEntry)
            {
                entry = selectedEntry;
            }

            if (entry == null)
            {
                e.Handled = true;
                return;
            }

            if (!ReferenceEquals(viewModel.SelectedQueueEntry, entry))
            {
                viewModel.SelectedQueueEntry = entry;
            }

            if (container?.ContextMenu != null)
            {
                container.ContextMenu.DataContext = viewModel;
            }
            else if (listView.ContextMenu != null)
            {
                listView.ContextMenu.DataContext = viewModel;
            }
        }

        private void SingersContextMenu_Opening(object sender, ContextMenuEventArgs e) { }

        private void SeekSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            try
            {
                var viewModel = DataContext as DJScreenViewModel;
                viewModel?.StartSeekingCommand.Execute(null);
                Log.Information("[DJSCREEN] Seek slider mouse down - seeking started");
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to handle seek slider mouse down: {Message}", ex.Message);
            }
        }

        private void SeekSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            StopSeekInteraction();
        }

        private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                if (DataContext is DJScreenViewModel viewModel && viewModel.IsSeeking)
                {
                    if (viewModel.SeekSongCommand.CanExecute(e.NewValue))
                    {
                        viewModel.SeekSongCommand.Execute(e.NewValue);
                        Log.Verbose("[DJSCREEN] Seek slider value changed: {Value}", e.NewValue);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to handle seek slider value change: {Message}", ex.Message);
            }
        }

        private void SeekSlider_DragStarted(object sender, DragStartedEventArgs e)
        {
            try
            {
                var viewModel = DataContext as DJScreenViewModel;
                viewModel?.StartSeekingCommand.Execute(null);
                Log.Information("[DJSCREEN] Seek slider drag started");
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to handle seek slider drag start: {Message}", ex.Message);
            }
        }

        private void SeekSlider_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            StopSeekInteraction();
        }

        private void StopSeekInteraction()
        {
            try
            {
                var viewModel = DataContext as DJScreenViewModel;
                viewModel?.StopSeekingCommand.Execute(null);
                Log.Information("[DJSCREEN] Seek interaction completed");
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to complete seek interaction: {Message}", ex.Message);
            }
        }

        private async void QueueListView_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            try
            {
                if (sender is ListViewItem item && item.DataContext is QueueEntryViewModel entry)
                {
                    if (DataContext is not DJScreenViewModel viewModel)
                    {
                        Log.Warning("[DJSCREEN] Double-click ignored: ViewModel is null");
                        return;
                    }

                    if (!viewModel.IsShowActive)
                    {
                        viewModel.SetWarningMessage("Start the show before playing a song.");
                        return;
                    }

                    viewModel.SelectedQueueEntry = entry;
                    Log.Information("[DJSCREEN] Double-click detected on QueueId={QueueId}, SongTitle={SongTitle}", entry.QueueId, entry.SongTitle);
                    await viewModel.PlayQueueEntryAsync(entry);
                    e.Handled = true;
                }
                else
                {
                    Log.Information("[DJSCREEN] Double-click ignored: No valid queue entry selected");
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to handle double-click: {Message}", ex.Message);
                MessageBox.Show($"Failed to handle double-click: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T target)
                {
                    return target;
                }
                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }
    }
}
