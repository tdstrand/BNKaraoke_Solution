using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Xaml.Behaviors; // Updated namespace
using BNKaraoke.DJ.ViewModels;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System.Windows.Media;

namespace BNKaraoke.DJ.Behaviors
{
    public class DragDropBehavior : Behavior<ListView>
    {
        public static readonly DependencyProperty DropCommandProperty =
            DependencyProperty.Register("DropCommand", typeof(IAsyncRelayCommand<DragEventArgs>), typeof(DragDropBehavior), new PropertyMetadata(null));

        public IAsyncRelayCommand<DragEventArgs> DropCommand
        {
            get { return (IAsyncRelayCommand<DragEventArgs>)GetValue(DropCommandProperty); }
            set { SetValue(DropCommandProperty, value); }
        }

        private bool _hasLoggedDragOver;
        private Point? _dragStartPoint;
        private QueueEntryViewModel? _pendingDragItem;
        private bool _isDragScheduled;

        protected override void OnAttached()
        {
            base.OnAttached();
            AssociatedObject.AllowDrop = true;
            AssociatedObject.DragOver += AssociatedObject_DragOver;
            AssociatedObject.Drop += AssociatedObject_Drop;
            AssociatedObject.MouseMove += AssociatedObject_MouseMove;
            AssociatedObject.PreviewMouseLeftButtonDown += AssociatedObject_PreviewMouseLeftButtonDown;
            AssociatedObject.PreviewMouseLeftButtonUp += AssociatedObject_PreviewMouseLeftButtonUp;
            Log.Information("[DRAGDROP BEHAVIOR] Attached to ListView");
        }

        protected override void OnDetaching()
        {
            AssociatedObject.DragOver -= AssociatedObject_DragOver;
            AssociatedObject.Drop -= AssociatedObject_Drop;
            AssociatedObject.MouseMove -= AssociatedObject_MouseMove;
            AssociatedObject.PreviewMouseLeftButtonDown -= AssociatedObject_PreviewMouseLeftButtonDown;
            AssociatedObject.PreviewMouseLeftButtonUp -= AssociatedObject_PreviewMouseLeftButtonUp;
            base.OnDetaching();
            Log.Information("[DRAGDROP BEHAVIOR] Detached from ListView");
        }

        private void AssociatedObject_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                _dragStartPoint = e.GetPosition(AssociatedObject);
                _pendingDragItem = FindQueueEntry(e.OriginalSource as DependencyObject);
            }
            catch (Exception ex)
            {
                Log.Error("[DRAGDROP BEHAVIOR] Failed to capture drag start: {Message}", ex.Message);
                ResetDragState();
            }
        }

        private void AssociatedObject_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            ResetDragState();
        }

        private void AssociatedObject_MouseMove(object sender, MouseEventArgs e)
        {
            try
            {
                if (e.LeftButton != MouseButtonState.Pressed)
                {
                    ResetDragState();
                    return;
                }

                if (_dragStartPoint == null || _pendingDragItem == null || _isDragScheduled)
                {
                    return;
                }

                var position = e.GetPosition(AssociatedObject);
                var horizontal = Math.Abs(position.X - _dragStartPoint.Value.X);
                var vertical = Math.Abs(position.Y - _dragStartPoint.Value.Y);
                if (horizontal < SystemParameters.MinimumHorizontalDragDistance &&
                    vertical < SystemParameters.MinimumVerticalDragDistance)
                {
                    return;
                }

                _isDragScheduled = true;
                AssociatedObject.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (_pendingDragItem == null)
                        {
                            return;
                        }

                        Log.Information("[DRAGDROP BEHAVIOR] Initiating drag for QueueId={QueueId}", _pendingDragItem.QueueId);
                        var data = new DataObject(typeof(QueueEntryViewModel), _pendingDragItem);
                        DragDrop.DoDragDrop(AssociatedObject, data, DragDropEffects.Move);
                    }
                    catch (Exception dragEx)
                    {
                        Log.Error("[DRAGDROP BEHAVIOR] Drag initiation failed: {Message}", dragEx.Message);
                    }
                    finally
                    {
                        ResetDragState();
                    }
                }), DispatcherPriority.Input);
            }
            catch (Exception ex)
            {
                Log.Error("[DRAGDROP BEHAVIOR] MouseMove failed: {Message}", ex.Message);
                ResetDragState();
            }
        }

        private void AssociatedObject_DragOver(object sender, DragEventArgs e)
        {
            try
            {
                if (!e.Data.GetDataPresent(typeof(QueueEntryViewModel)))
                {
                    e.Effects = DragDropEffects.None;
                    e.Handled = true;
                    return;
                }

                e.Effects = DragDropEffects.Move;
                e.Handled = true;

                if (!_hasLoggedDragOver)
                {
                    Log.Information("[DRAGDROP BEHAVIOR] DragOver: Effects=\"{Effects}\"", e.Effects);
                    _hasLoggedDragOver = true;
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DRAGDROP BEHAVIOR] DragOver failed: {Message}", ex.Message);
            }
        }

        private async void AssociatedObject_Drop(object sender, DragEventArgs e)
        {
            try
            {
                Log.Information("[DRAGDROP BEHAVIOR] Drop initiated");
                if (DropCommand != null && DropCommand.CanExecute(e))
                {
                    await DropCommand.ExecuteAsync(e);
                    Log.Information("[DRAGDROP BEHAVIOR] DropCommand executed");
                }
                _hasLoggedDragOver = false; // Reset for next drag
            }
            catch (Exception ex)
            {
                Log.Error("[DRAGDROP BEHAVIOR] Drop failed: {Message}", ex.Message);
            }
        }

        private QueueEntryViewModel? FindQueueEntry(DependencyObject? origin)
        {
            while (origin != null)
            {
                if (origin is ListViewItem listViewItem)
                {
                    return listViewItem.DataContext as QueueEntryViewModel;
                }
                origin = VisualTreeHelper.GetParent(origin);
            }

            return AssociatedObject?.SelectedItem as QueueEntryViewModel;
        }

        private void ResetDragState()
        {
            _dragStartPoint = null;
            _pendingDragItem = null;
            _isDragScheduled = false;
        }
    }
}
