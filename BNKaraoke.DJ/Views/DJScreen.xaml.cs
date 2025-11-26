using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using CommunityToolkit.Mvvm.Input;
using BNKaraoke.DJ.Services;
using BNKaraoke.DJ.ViewModels;
using Serilog;

namespace BNKaraoke.DJ.Views
{
    /// <summary>
    /// Interaction logic for DJScreen.xaml
    /// </summary>
    public partial class DJScreen
    {
        private DJScreenViewModel ViewModel => (DJScreenViewModel)DataContext;
        private static readonly TimeSpan QueueDoubleClickSuppression = TimeSpan.FromMilliseconds(250);
        private DateTime _lastQueueDoubleClickUtc = DateTime.MinValue;
        private DateTime _lastQueueClickUtc = DateTime.MinValue;
        private Point _lastQueueClickPoint;
        private bool _isSeekSliderInteracting;

        public DJScreen()
        {
            InitializeComponent();
            DataContextChanged += DJScreen_DataContextChanged;
            Loaded += DJScreen_Loaded;
            PreviewMouseDown += DJScreen_PreviewMouseDown;
            PreviewMouseUp += DJScreen_PreviewMouseUp;

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

        private async void PlayButton_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            e.Handled = true;
            await ExecuteCommandAsync(ViewModel?.PlayCommand, null, nameof(DJScreenViewModel.PlayCommand));
        }

        private async void StopRestartButton_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            e.Handled = true;
            await ExecuteCommandAsync(ViewModel?.StopRestartCommand, null, nameof(DJScreenViewModel.StopRestartCommand));
        }

        private async void ToggleShowButton_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            e.Handled = true;
            await ExecuteCommandAsync(ViewModel?.ToggleShowCommand, null, nameof(DJScreenViewModel.ToggleShowCommand));
        }

        private async void ToggleAutoPlayButton_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            e.Handled = true;
            await ExecuteCommandAsync(ViewModel?.ToggleAutoPlayCommand, null, nameof(DJScreenViewModel.ToggleAutoPlayCommand));
        }

        private async void CommandButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            if (sender is not Button button)
            {
                return;
            }

            e.Handled = true;
            await ExecuteCommandAsync(button.Command, button.CommandParameter, button.Name ?? button.Content?.ToString() ?? "<command>");
        }

        private void ListViewItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListViewItem item && !item.IsSelected)
            {
                item.IsSelected = true;
            }
        }

        private void ListViewItem_PreviewMouse(object sender, MouseEventArgs e) { }

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

        private async void SeekSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            _isSeekSliderInteracting = true;
            await EnsureSeekingModeAsync();
        }

        private async void SeekSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            if (sender is Slider slider)
            {
                await ExecuteCommandAsync(ViewModel?.SeekSongCommand, slider.Value, nameof(DJScreenViewModel.SeekSongCommand));
            }

            await ExecuteCommandAsync(ViewModel?.StopSeekingCommand, null, nameof(DJScreenViewModel.StopSeekingCommand));
            _isSeekSliderInteracting = false;
        }

        private async void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isSeekSliderInteracting)
            {
                return;
            }

            await EnsureSeekingModeAsync();
            await ExecuteCommandAsync(ViewModel?.SeekSongCommand, e.NewValue, nameof(DJScreenViewModel.SeekSongCommand));
        }

        private async Task EnsureSeekingModeAsync()
        {
            if (ViewModel == null)
            {
                return;
            }

            if (!ViewModel.IsSeeking)
            {
                await ExecuteCommandAsync(ViewModel.StartSeekingCommand, null, nameof(DJScreenViewModel.StartSeekingCommand));
            }
        }

        private async Task ExecuteCommandAsync(ICommand? command, object? parameter, string source)
        {
            if (command == null)
            {
                Log.Warning("[DJSCREEN UI] {Source} has no command to execute", source);
                return;
            }

            try
            {
                if (command is IAsyncRelayCommand asyncRelay)
                {
                    if (!asyncRelay.CanExecute(parameter))
                    {
                        Log.Warning("[DJSCREEN UI] Command {Source} cannot execute (async path)", source);
                        return;
                    }

                    await asyncRelay.ExecuteAsync(parameter);
                }
                else
                {
                    if (!command.CanExecute(parameter))
                    {
                        Log.Warning("[DJSCREEN UI] Command {Source} cannot execute (sync path)", source);
                        return;
                    }

                    command.Execute(parameter);
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN UI] Failed to execute command for {Source}: {Message}", source, ex.Message);
            }
        }

        private async void QueueListView_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            await HandleQueueListDoubleClickAsync(sender, e);
        }

        private async void QueueListView_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            var now = DateTime.UtcNow;
            var targetElement = QueueItemsListView as IInputElement ?? sender as IInputElement ?? this;
            var position = e.GetPosition(targetElement);

            var elapsed = now - _lastQueueClickUtc;
            var doubleClickSize = Forms.SystemInformation.DoubleClickSize;
            var withinTime = elapsed.TotalMilliseconds <= Forms.SystemInformation.DoubleClickTime;
            var withinWidth = Math.Abs(position.X - _lastQueueClickPoint.X) <= doubleClickSize.Width;
            var withinHeight = Math.Abs(position.Y - _lastQueueClickPoint.Y) <= doubleClickSize.Height;
            var isDoubleClick = withinTime && withinWidth && withinHeight;

            _lastQueueClickUtc = now;
            _lastQueueClickPoint = position;

            if (isDoubleClick)
            {
                Log.Information("[DJSCREEN UI] Detected double-click via PreviewMouseLeftButtonUp fallback (Handled={Handled})", e.Handled);
                await HandleQueueListDoubleClickAsync(sender, e);
            }
        }

        private async void QueueListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            await HandleQueueListDoubleClickAsync(sender, e);
        }

        private async Task HandleQueueListDoubleClickAsync(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if (now - _lastQueueDoubleClickUtc < QueueDoubleClickSuppression)
            {
                Log.Debug("[DJSCREEN UI] Ignoring duplicate double-click within suppression window");
                return;
            }
            _lastQueueDoubleClickUtc = now;

            Log.Information("[DJSCREEN UI] Mouse double-click detected on queue (Handled={Handled})", e.Handled);
            e.Handled = true;

            var container = sender as ListViewItem ?? FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject);
            if (container == null)
            {
                Log.Warning("[DJSCREEN UI] Double-click without ListViewItem (OriginalSource={Source})", e.OriginalSource?.GetType().Name ?? "<null>");
                return;
            }

            if (container.DataContext is not QueueEntryViewModel entry)
            {
                Log.Warning("[DJSCREEN UI] Double-click item missing QueueEntryViewModel (DataContext={Type})", container.DataContext?.GetType().Name ?? "<null>");
                return;
            }

            if (DataContext is not DJScreenViewModel viewModel)
            {
                Log.Warning("[DJSCREEN UI] Double-click but ViewModel missing");
                return;
            }

            Log.Information("[DJSCREEN UI] Double-click resolved QueueId={QueueId}", entry.QueueId);

            if (!container.IsSelected)
            {
                container.IsSelected = true;
            }

            if (!ReferenceEquals(viewModel.SelectedQueueEntry, entry))
            {
                viewModel.SelectedQueueEntry = entry;
            }

            if (!viewModel.IsShowActive)
            {
                viewModel.SetWarningMessage("Start the show before playing a song.");
                return;
            }

            try
            {
                Log.Information("[DJSCREEN UI] Double-click invoking PlayQueueEntryAsync for QueueId={QueueId}", entry.QueueId);
                await viewModel.PlayQueueEntryAsync(entry);
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to start playback from double-click: {Message}", ex.Message);
            }
        }

        private void DJScreen_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            Log.Information("[DJSCREEN UI] PreviewMouseDown Source={Source}, Button={Button}, Handled={Handled}",
                e.Source?.GetType().Name ?? "<null>",
                e.ChangedButton,
                e.Handled);
        }

        private void DJScreen_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            Log.Information("[DJSCREEN UI] PreviewMouseUp Source={Source}, Button={Button}, Handled={Handled}",
                e.Source?.GetType().Name ?? "<null>",
                e.ChangedButton,
                e.Handled);
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
