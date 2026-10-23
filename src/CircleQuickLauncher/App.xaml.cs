using System.IO.Pipes;
using System.Text;
using CircleQuickLauncher.Services;
using Microsoft.UI.Xaml;

namespace CircleQuickLauncher;

/// <summary>
/// Application entry point: single-instance enforcement, crash logging, main window.
/// </summary>
public partial class App : Application
{
    public const string InstanceMutexName = @"Local\CircleQuickLauncher_9F2A7C1E";
    public const string ActivationPipeName = "CircleQuickLauncher.Activation_9F2A7C1E";

    private Mutex? _instanceMutex;
    private CancellationTokenSource? _pipeCts;
    private Window? _window;

    public App()
    {
        try
        {
            Directory.CreateDirectory(AppState.LogsDirectory);
            File.AppendAllText(
                Path.Combine(AppState.LogsDirectory, "startup.log"),
                $"{DateTime.Now:o} App.ctor pid={Environment.ProcessId}{Environment.NewLine}");
        }
        catch (Exception)
        {
        }

        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    public static Window? MainWindowInstance { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(AppState.LogsDirectory, "startup.log"),
                $"{DateTime.Now:o} OnLaunched{Environment.NewLine}");
        }
        catch (Exception)
        {
        }

        _instanceMutex = new Mutex(initiallyOwned: true, name: InstanceMutexName, createdNew: out bool createdNew);

        try
        {
            File.AppendAllText(
                Path.Combine(AppState.LogsDirectory, "startup.log"),
                $"{DateTime.Now:o} singleInstance={createdNew}{Environment.NewLine}");
        }
        catch (Exception)
        {
        }

        if (!createdNew)
        {
            // Another instance is running: ask it to surface its settings window, then quit.
            TrySignalExistingInstance();
            ExitApp();
            return;
        }

        StartActivationListener();

        try
        {
            AutorunService.RepairIfBroken();
        }
        catch (Exception)
        {
            // Startup repair is best-effort and must never block launch.
        }

        try
        {
            _window = new MainWindow();
            MainWindowInstance = _window;
            _window.Activate();
            ((MainWindow)_window).FinalizeInterop();
            _window.DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => ((MainWindow)_window).FinalizeInterop());
            File.AppendAllText(
                Path.Combine(AppState.LogsDirectory, "startup.log"),
                $"{DateTime.Now:o} window activated{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            File.AppendAllText(
                Path.Combine(AppState.LogsDirectory, "startup.log"),
                $"{DateTime.Now:o} WINDOW FAILURE: {ex}{Environment.NewLine}");
            throw;
        }
    }

    public void RequestShowSettings()
    {
        _window?.DispatcherQueue.TryEnqueue(() =>
        {
            if (_window is MainWindow mainWindow)
            {
                mainWindow.ShowSettingsWindow();
            }
        });
    }

    public void ExitApp()
    {
        _pipeCts?.Cancel();
        try
        {
            _instanceMutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }

        _instanceMutex?.Dispose();
        _instanceMutex = null;

        Exit();
    }

    private static void TrySignalExistingInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".", ActivationPipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            client.Connect(timeout: 1500);
            byte[] buffer = Encoding.ASCII.GetBytes("ACTIVATE");
            client.Write(buffer, 0, buffer.Length);
            client.Flush();
        }
        catch (Exception)
        {
            // The primary instance may still be starting; exiting is still correct.
        }
    }

    private void StartActivationListener()
    {
        _pipeCts = new CancellationTokenSource();
        CancellationToken token = _pipeCts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        ActivationPipeName,
                        PipeDirection.In,
                        maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token);

                    var buffer = new byte[32];
                    int read = await server.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
                    if (read > 0)
                    {
                        RequestShowSettings();
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException)
                {
                    await Task.Delay(250, token).ContinueWith(_ => { }, TaskScheduler.Default);
                }
                catch (Exception)
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    await Task.Delay(500, CancellationToken.None);
                }
            }
        }, token);
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        CrashLog.Write(e.Exception);
        // Configuration writes are atomic, so swallowing here cannot corrupt user data.
        // The exception is recorded for diagnostics instead of tearing down the shell unexpectedly.
        e.Handled = true;
    }
}
