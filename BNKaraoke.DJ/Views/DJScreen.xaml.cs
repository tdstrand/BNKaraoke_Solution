using BNKaraoke.DJ.Models;
using BNKaraoke.DJ.Services;
using BNKaraoke.DJ.ViewModels;
using Serilog;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Threading.Tasks;

namespace BNKaraoke.DJ.Views
{
    public partial class DJScreen : Window
    {
        public DJScreen()
        {
            InitializeComponent();
            try
            {
                DataContext = new DJScreenViewModel();

                // Suppress system beeps for unhandled keys at the window level
                PreviewKeyDown += DJScreen_PreviewKeyDown;
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to initialize DJScreen: {Message}", ex.Message);
                MessageBox.Show($"Failed to initialize DJScreen: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.None);
                Close();
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var viewModel = DataContext as DJScreenViewModel;
                if (viewModel == null)
                {
                    Log.Error("[DJSCREEN] Failed to load ViewModel");
                    MessageBox.Show("Failed to load ViewModel.", "Error", MessageBoxButton.OK, MessageBoxImage.None);
                    Close();
                    return;
                }

                var settings = SettingsService.Instance.Settings;
                if (settings.MaximizedOnStart)
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
                MessageBox.Show($"Failed to load DJScreen: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.None);
                Close();
            }
        }

        private void ListViewItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is ListViewItem item && item.IsSelected)
                {
                    if (item.DataContext is QueueEntryViewModel queueEntry)
                    {
                        var viewModel = DataContext as DJScreenViewModel;
                        if (viewModel != null)
                        {
                            viewModel.SelectedQueueEntry = queueEntry;
                            viewModel.StartDragCommand.Execute(queueEntry);
                            Log.Information("[DJSCREEN] Drag initiated for QueueId={QueueId}", queueEntry.QueueId);
                        }
                        else
                        {
                            Log.Warning("[DJSCREEN] ViewModel is null in drag handler");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to initiate drag: {Message}", ex.Message);
                MessageBox.Show($"Failed to initiate drag: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.None);
            }
        }

        private async void QueueListView_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is ListViewItem item && item.DataContext is QueueEntryViewModel queueEntry)
                {
                    var viewModel = DataContext as DJScreenViewModel;
                    if (viewModel == null)
                    {
                        Log.Warning("[DJSCREEN] Double-click ignored: ViewModel is null");
                        return;
                    }
                    Log.Information("[DJSCREEN] Double-click detected on QueueId={QueueId}, SongTitle={SongTitle}", queueEntry.QueueId, queueEntry.SongTitle);
                    await viewModel.PlayQueueEntryAsync(queueEntry);
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
                MessageBox.Show($"Failed to handle double-click: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.None);
            }
        }

        private void QueueListView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            try
            {
                if (sender is not ListView listView)
                {
                    return;
                }

                if (e.OriginalSource is DependencyObject source)
                {
                    if (ItemsControl.ContainerFromElement(listView, source) is ListViewItem item)
                    {
                        listView.SelectedItem = item.DataContext;
                        if (DataContext is DJScreenViewModel viewModel && item.DataContext is QueueEntryViewModel queueEntry)
                        {
                            viewModel.SelectedQueueEntry = queueEntry;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed during queue context menu opening: {Message}", ex.Message);
            }
        }

        private void SingersContextMenu_Opening(object sender, ContextMenuEventArgs e)
        {
            try
            {
                if (sender is ListView listView && listView.SelectedItem is Singer selectedSinger)
                {
                    var viewModel = DataContext as DJScreenViewModel;
                    if (viewModel == null)
                    {
                        Log.Warning("[DJSCREEN] Singers ContextMenu: ViewModel is null");
                        e.Handled = true;
                        return;
                    }
                    var contextMenu = listView.ContextMenu;
                    if (contextMenu == null)
                    {
                        Log.Warning("[DJSCREEN] Singers ContextMenu: ContextMenu is null");
                        e.Handled = true;
                        return;
                    }
                    if (!SettingsService.Instance.Settings.TestMode)
                    {
                        Log.Information("[DJSCREEN] Singers ContextMenu: TestMode=false, menu disabled");
                        e.Handled = true;
                        return;
                    }
                    Log.Information("[DJSCREEN] Opening context menu for singer: UserId={UserId}, DisplayName={DisplayName}", selectedSinger.UserId, selectedSinger.DisplayName);
                    foreach (var item in contextMenu.Items)
                    {
                        if (item is MenuItem menuItem)
                        {
                            menuItem.Click -= (s, args) => { };
                            menuItem.Click += (s, args) =>
                            {
                                try
                                {
                                    Log.Information("[DJSCREEN] MenuItem clicked: Name={Name}", menuItem.Name);
                                    string status = menuItem.Name switch
                                    {
                                        "SetAvailableMenuItem" => "Active",
                                        "SetOnBreakMenuItem" => "OnBreak",
                                        "SetNotJoinedMenuItem" => "NotJoined",
                                        "SetLoggedOutMenuItem" => "LoggedOut",
                                        _ => string.Empty
                                    };
                                    if (!string.IsNullOrEmpty(status))
                                    {
                                        var parameter = $"{status}|{selectedSinger.UserId}";
                                        Log.Information("[DJSCREEN] Right-click operation: User={DisplayName}, UserId={UserId}, NewStatus={Status}",
                                            selectedSinger.DisplayName, selectedSinger.UserId, status);
                                        viewModel.UpdateSingerStatusCommand.Execute(parameter);
                                    }
                                    else
                                    {
                                        Log.Warning("[DJSCREEN] Unknown MenuItem: {Name}", menuItem.Name);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log.Error("[DJSCREEN] Failed to handle MenuItem click: {Message}", ex.Message);
                                    MessageBox.Show($"Failed to update singer status: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.None);
                                }
                            };
                        }
                    }
                }
                else
                {
                    Log.Information("[DJSCREEN] Singers ContextMenu: No singer selected or invalid sender");
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to open Singers ContextMenu: {Message}", ex.Message);
                MessageBox.Show($"Failed to open context menu: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.None);
                e.Handled = true;
            }
        }

        private void Slider_DragStarted(object sender, DragStartedEventArgs e)
        {
            try
            {
                var viewModel = DataContext as DJScreenViewModel;
                if (viewModel != null)
                {
                    viewModel.StartSeekingCommand.Execute(null);
                    Log.Information("[DJSCREEN] Slider drag started");
                }
                else
                {
                    Log.Warning("[DJSCREEN] Slider drag started: ViewModel is null");
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to handle slider drag start: {Message}", ex.Message);
            }
        }

        private void Slider_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            try
            {
                var viewModel = DataContext as DJScreenViewModel;
                if (viewModel != null)
                {
                    viewModel.StopSeekingCommand.Execute(null);
                    Log.Information("[DJSCREEN] Slider drag completed");
                }
                else
                {
                    Log.Warning("[DJSCREEN] Slider drag completed: ViewModel is null");
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to handle slider drag complete: {Message}", ex.Message);
            }
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                var viewModel = DataContext as DJScreenViewModel;
                if (viewModel != null)
                {
                    if (viewModel.IsSeeking)
                    {
                        viewModel.SeekSongCommand.Execute(e.NewValue);
                        Log.Information("[DJSCREEN] Slider value changed: NewValue={NewValue}", e.NewValue);
                    }
                }
                else
                {
                    Log.Warning("[DJSCREEN] Slider value changed: ViewModel is null");
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to handle slider value change: {Message}", ex.Message);
            }
        }

        private void Slider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var viewModel = DataContext as DJScreenViewModel;
                viewModel?.StartSeekingCommand.Execute(null);
                Log.Information("[DJSCREEN] Slider mouse down - seeking started");
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to handle slider mouse down: {Message}", ex.Message);
            }
        }

        private void Slider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var viewModel = DataContext as DJScreenViewModel;
                viewModel?.StopSeekingCommand.Execute(null);
                Log.Information("[DJSCREEN] Slider mouse up - seeking stopped");
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to handle slider mouse up: {Message}", ex.Message);
            }
        }

        private void DJScreen_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!ShouldSuppressKey(e) || e.Handled)
            {
                return;
            }

            if (Keyboard.FocusedElement is IInputElement focusedElement &&
                FocusedElementHandlesKey(focusedElement, e))
            {
                return;
            }

            e.Handled = true;
        }

        private static bool ShouldSuppressKey(KeyEventArgs e)
        {
            return e.Key is Key.Enter or Key.Return or Key.Escape or Key.Tab;
        }

        private static bool FocusedElementHandlesKey(IInputElement focusedElement, KeyEventArgs e)
        {
            switch (focusedElement)
            {
                case TextBoxBase:
                case PasswordBox:
                    return true;
                case ComboBox:
                    return true;
                case Selector selector:
                    return selector.IsEnabled;
                case ButtonBase buttonBase:
                    if (!buttonBase.IsEnabled || !IsCommandExecutable(buttonBase))
                    {
                        e.Handled = true;
                    }
                    return true;
                case UIElement uiElement when uiElement.IsEnabled && uiElement.Focusable:
                    if (e.Key == Key.Tab)
                    {
                        return true;
                    }
                    break;
            }

            return false;
        }

        private static bool IsCommandExecutable(ButtonBase buttonBase)
        {
            if (buttonBase is not ICommandSource commandSource)
            {
                return buttonBase.IsEnabled;
            }

            var command = commandSource.Command;
            if (command == null)
            {
                return buttonBase.IsEnabled;
            }

            var parameter = commandSource.CommandParameter;
            var target = commandSource.CommandTarget ?? buttonBase;

            try
            {
                return command switch
                {
                    RoutedCommand routedCommand => routedCommand.CanExecute(parameter, target),
                    _ => command.CanExecute(parameter)
                };
            }
            catch
            {
                return false;
            }
        }
    }
}
