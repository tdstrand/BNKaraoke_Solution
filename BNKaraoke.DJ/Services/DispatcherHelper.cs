using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace BNKaraoke.DJ.Services
{
    public static class DispatcherHelper
    {
        public static void RunOnUIThread(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                action();
            else
                dispatcher.Invoke(action);
        }

        public static async Task RunOnUIThreadAsync(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            await dispatcher.InvokeAsync(action);
        }
    }
}
