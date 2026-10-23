using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace PolyView
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static string CrashLogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PolyView", "crash.log");

        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                LogException("AppDomain", args.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                LogException("Task", args.Exception);
                args.SetObserved();
            };
            base.OnStartup(e);
        }

        static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogException("Dispatcher", e.Exception);
            MessageBox.Show(e.Exception.Message + "\n\n" + CrashLogPath, "PolyView", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        /// <summary>Append an exception (with inner exceptions and stack) to %LOCALAPPDATA%\PolyView\crash.log.</summary>
        public static void LogException(string source, Exception? ex)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
                File.AppendAllText(CrashLogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n{ex}\n\n");
            }
            catch (Exception)
            {
            }
        }
    }

}
