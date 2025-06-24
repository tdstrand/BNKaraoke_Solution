// DJScreen.xaml.cs
using BNKaraoke.DJ.Models;
using BNKaraoke.DJ.Services;
using BNKaraoke.DJ.ViewModels;
using Serilog;
using System;
using System.Windows;
using System.Windows.Controls;
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
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to initialize DJScreen: {Message}", ex.Message);
                MessageBox.Show($"Failed to initialize DJScreen: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    MessageBox.Show("Failed to load ViewModel.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Close();
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to load DJScreen: {Message}", ex.Message);
                MessageBox.Show($"Failed to load DJScreen: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        private void ListViewItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is ListViewItem item && item.IsSelected)
                {
                    var queueEntry = item.DataContext as QueueEntry;
                    if (queueEntry != null)
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
                MessageBox.Show($"Failed to initiate drag: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void QueueListView_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is ListViewItem item && item.DataContext is QueueEntry queueEntry)
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
                MessageBox.Show($"Failed to handle double-click: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                                    MessageBox.Show($"Failed to update singer status: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show($"Failed to open context menu: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            }
        }
    }
}