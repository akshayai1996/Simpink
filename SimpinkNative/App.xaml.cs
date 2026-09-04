using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using SimpinkNative.Interop;
using SimpinkNative.Windows;

namespace SimpinkNative
{
    public partial class App : Application
    {
        private OverlayWindow? _overlay;
        private ToolbarWindow? _toolbar;
        private Mutex? _singleInstanceMutex;

        public static void LogCrash(Exception? ex)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimpinkNative");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "crash.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
            }
            catch { }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                LogCrash(args.ExceptionObject as Exception);

            DispatcherUnhandledException += (_, args) =>
            {
                LogCrash(args.Exception);
                MessageBox.Show($"Unhandled: {args.Exception.Message}", "Simpink", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            try
            {
                bool createdNew;
                _singleInstanceMutex = new Mutex(true, "Global\\SimpinkNativeSingleInstance", out createdNew);
                if (!createdNew)
                {
                    MessageBox.Show("Simpink is already running.", "Simpink", MessageBoxButton.OK, MessageBoxImage.Information);
                    Shutdown();
                    return;
                }

                Win32.SetProcessDPIAware();

                base.OnStartup(e);
            }
            catch (Exception ex)
            {
                LogCrash(ex);
                MessageBox.Show($"OnStartup error: {ex.Message}", "Simpink", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            try
            {
                _toolbar = new ToolbarWindow();
                _overlay = new OverlayWindow(_toolbar);
                _toolbar.SetOverlay(_overlay);

                _overlay.Show();
                _toolbar.Owner = _overlay;
                _toolbar.Show();

                _toolbar.Activate();
            }
            catch (Exception ex)
            {
                LogCrash(ex);
                MessageBox.Show($"Startup error: {ex}", "Simpink", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            _overlay?.Close();
            _toolbar?.Close();
            try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
            _singleInstanceMutex?.Dispose();
        }
    }
}
