using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace BNKaraoke.DJ.Tests
{
    internal static class WpfTestHelper
    {
        public static Task RunAsync(Func<Task> action)
        {
            var completion = new TaskCompletionSource<object?>();

            var thread = new Thread(() =>
            {
                try
                {
                    SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                    if (Application.Current == null)
                    {
                        new Application();
                    }

                    Dispatcher.CurrentDispatcher.InvokeAsync(async () =>
                    {
                        try
                        {
                            await action();
                            completion.TrySetResult(null);
                        }
                        catch (Exception ex)
                        {
                            completion.TrySetException(ex);
                        }
                        finally
                        {
                            Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.ApplicationIdle);
                        }
                    });

                    Dispatcher.Run();
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            })
            {
                IsBackground = true
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            return completion.Task;
        }

        public static Task RunAsync(Action action)
        {
            return RunAsync(() =>
            {
                action();
                return Task.CompletedTask;
            });
        }

        public static async Task WaitForIdleAsync(Dispatcher dispatcher)
        {
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        }

        public static Task WaitForLoadedAsync(FrameworkElement element)
        {
            if (element.IsLoaded)
            {
                return Task.CompletedTask;
            }

            var tcs = new TaskCompletionSource<object?>();
            RoutedEventHandler? handler = null;
            handler = (_, _) =>
            {
                element.Loaded -= handler;
                tcs.TrySetResult(null);
            };
            element.Loaded += handler;
            return tcs.Task;
        }

        public static async Task<bool> WaitForConditionAsync(Func<bool> predicate, Dispatcher dispatcher, TimeSpan timeout)
        {
            var start = DateTime.UtcNow;
            while (DateTime.UtcNow - start < timeout)
            {
                if (predicate())
                {
                    return true;
                }

                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                await Task.Delay(20);
            }

            return predicate();
        }
    }
}
