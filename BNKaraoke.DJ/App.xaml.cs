using BNKaraoke.DJ.Services;
using BNKaraoke.DJ.ViewModels;
using BNKaraoke.DJ.Views;
using Serilog;
using Serilog.Debugging;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;

namespace BNKaraoke.DJ;

public partial class App : Application
{
    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Enable Serilog self-logging for errors
        SelfLog.Enable(msg => System.Diagnostics.Debug.WriteLine($"[Serilog Error] {msg}"));

        // Initialize SettingsService asynchronously
        var settingsService = SettingsService.Instance;
        try
        {
            await settingsService.LoadSettingsAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[APP START] Failed to load settings: {ex.Message}");
            MessageBox.Show($"Failed to initialize settings: {ex.Message}. Using defaults.", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        // Allocate console for dotnet run if ShowDebugConsole is enabled
        if (settingsService.Settings.ShowDebugConsole)
        {
            AllocConsole();
        }

        // Use configured log file path
        var logPath = settingsService.Settings.LogFilePath;
        var logDir = Path.GetDirectoryName(logPath);
        if (string.IsNullOrEmpty(logDir))
        {
            logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BNKaraoke", "logs.txt");
            logDir = Path.GetDirectoryName(logPath);
            System.Diagnostics.Debug.WriteLine($"[APP START] Invalid configured log path, using fallback: {logPath}");
        }
        if (!string.IsNullOrEmpty(logDir))
        {
            try
            {
                Directory.CreateDirectory(logDir);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[APP START] Failed to create log directory {logDir}: {ex.Message}");
                logPath = Path.Combine(Path.GetTempPath(), "BNKaraoke", "logs.txt");
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[APP START] Invalid log path directory: {logPath}");
        }

        try
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Is(settingsService.Settings.EnableVerboseLogging ? Serilog.Events.LogEventLevel.Verbose : Serilog.Events.LogEventLevel.Information)
                .WriteTo.Console()
                .WriteTo.Debug()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            Log.Information("[APP START] Serilog initialized, log file: {LogPath}", logPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[APP START] Failed to initialize Serilog: {ex.Message}");
            MessageBox.Show($"Failed to initialize logging: {ex.Message}. Check console for details.", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        // Add global exception handler
        DispatcherUnhandledException += (s, args) =>
        {
            Log.Error("[APP] Unhandled dispatcher exception: {Message}, StackTrace={StackTrace}",
                args.Exception.Message, args.Exception.StackTrace);
            MessageBox.Show($"An unexpected error occurred: {args.Exception.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        base.OnStartup(e);

        var userSessionService = UserSessionService.Instance;
        var mainWindow = new DJScreen { WindowState = settingsService.Settings.MaximizedOnStart ? WindowState.Maximized : WindowState.Normal };

        Log.Information("[APP START] Checking session: IsAuthenticated={IsAuthenticated}", userSessionService.IsAuthenticated);

        mainWindow.Show(); // Show DJScreen first

        if (!userSessionService.IsAuthenticated)
        {
            Log.Information("[APP START] Showing LoginWindow as dialog");
            var loginWindow = new LoginWindow { Owner = mainWindow, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var result = loginWindow.ShowDialog();
            if (result != true)
            {
                Log.Information("[APP START] Login canceled, shutting down");
                Shutdown();
                return;
            }
            Log.Information("[APP START] Login succeeded");
        }

        // Trigger DJScreenViewModel refresh
        if (mainWindow.DataContext is DJScreenViewModel viewModel)
        {
            await viewModel.UpdateAuthenticationState();
            Log.Information("[APP START] Triggered DJScreenViewModel refresh post-login");
        }

        Log.Information("[APP START] Activating DJScreen");
        MainWindow = mainWindow; // Set after login to ensure proper WPF lifecycle
        mainWindow.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("[APP EXIT] Application shutting down");
        var userSessionService = UserSessionService.Instance;
        if (userSessionService.IsAuthenticated)
        {
            Log.Information("[APP EXIT] User is authenticated, logging out and leaving event");
            userSessionService.ClearSession();
        }
        base.OnExit(e);
        Log.CloseAndFlush();
    }
}