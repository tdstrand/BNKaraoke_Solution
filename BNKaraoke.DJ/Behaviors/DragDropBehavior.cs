using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Xaml.Behaviors; // Updated namespace
using BNKaraoke.DJ.Models;
using CommunityToolkit.Mvvm.Input;
using Serilog;

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

        protected override void OnAttached()
        {
            base.OnAttached();
            AssociatedObject.AllowDrop = true;
            AssociatedObject.DragOver += AssociatedObject_DragOver;
            AssociatedObject.Drop += AssociatedObject_Drop;
            AssociatedObject.MouseMove += AssociatedObject_MouseMove;
            Log.Information("[DRAGDROP BEHAVIOR] Attached to ListView");
        }

        protected override void OnDetaching()
        {
            AssociatedObject.DragOver -= AssociatedObject_DragOver;
            AssociatedObject.Drop -= AssociatedObject_Drop;
            AssociatedObject.MouseMove -= AssociatedObject_MouseMove;
            base.OnDetaching();
            Log.Information("[DRAGDROP BEHAVIOR] Detached from ListView");
        }

        private void AssociatedObject_MouseMove(object sender, MouseEventArgs e)
        {
            try
            {
                if (e.LeftButton == MouseButtonState.Pressed && AssociatedObject.SelectedItem != null)
                {
                    var draggedItem = AssociatedObject.SelectedItem as QueueEntry;
                    if (draggedItem != null)
                    {
                        Log.Information("[DRAGDROP BEHAVIOR] Initiating drag for QueueId={QueueId}", draggedItem.QueueId);
                        var data = new DataObject(typeof(QueueEntry), draggedItem);
                        DragDrop.DoDragDrop(AssociatedObject, data, DragDropEffects.Move);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DRAGDROP BEHAVIOR] MouseMove failed: {Message}", ex.Message);
            }
        }

        private void AssociatedObject_DragOver(object sender, DragEventArgs e)
        {
            try
            {
                if (!e.Data.GetDataPresent(typeof(QueueEntry)))
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
    }
}