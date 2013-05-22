using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;

namespace MediaBrowser.Server.Installer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        protected bool SystemClosing = false;

        protected WebClient MainClient = new WebClient();

        public MainWindow()
        {
            if (!InstallUtil.Installer.IsAdmin)
            {
                RunAsAdmin();
            }
            else
            {
                InitializeComponent();
                var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MediaBrowser-InstallLogs");
                if (!Directory.Exists(logPath)) Directory.CreateDirectory(logPath);
                var logFile = Path.Combine(logPath, "server-install.log");
                if (File.Exists(logFile)) File.Delete(logFile);
                Trace.Listeners.Add(new TextWriterTraceListener(logFile));
                Trace.AutoFlush = true;
                var request = InstallUtil.Installer.ParseArgsAndWait(Environment.GetCommandLineArgs());
                request.ReportStatus = UpdateStatus;
                request.Progress = new ProgressUpdater(this);
                request.WebClient = MainClient;
                Trace.TraceInformation("Creating install session for {0}", request.Product);
                DoInstall(new InstallUtil.Installer(request)); // fire and forget so we get our window up
            }
        }

        private void RunAsAdmin()
        {
            var info = new ProcessStartInfo
            {
                FileName = Process.GetCurrentProcess().MainModule.FileName,
                Arguments = string.Join(" ", Environment.GetCommandLineArgs().Skip(1)) + " admin=true",
                Verb = "runas"
            };

            Process.Start(info);
            
            SystemClose();
        }

        private class ProgressUpdater : IProgress<double>
        {
            private readonly MainWindow _mainWindow;
            public ProgressUpdater(MainWindow win)
            {
                _mainWindow = win;
            }

            public void Report(double value)
            {
                _mainWindow.ReportProgress(value);
            }
        }

        public void ReportProgress(double value)
        {
            rectProgress.Width = (this.Width * value) / 100f;
        }

        private void UpdateStatus(string message)
        {
            lblStatus.Text = message;
        }

        private async Task DoInstall(InstallUtil.Installer installer)
        {
            var result = await installer.DoInstall();
            if (!result.Success)
            {
                SystemClose(result.Message + "\n\n" + result.Exception.GetType() + "\n" + result.Exception.Message);
            }
            else
            {
                SystemClose();
            }
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            Trace.TraceInformation("Installation Requested to be Cancelled by user.");
            this.Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!SystemClosing && MessageBox.Show("Cancel Installation - Are you sure?", "Cancel", MessageBoxButton.YesNo) == MessageBoxResult.No)
            {
                e.Cancel = true;
                return;
            }

            if (MainClient.IsBusy)
            {
                MainClient.CancelAsync();
                while (MainClient.IsBusy)
                {
                    // wait to finish
                }
            }
            MainClient.Dispose();
            InstallUtil.Installer.ClearTempLocation();
            base.OnClosing(e);
        }

        protected void SystemClose(string message = null)
        {
            if (message != null)
            {
                MessageBox.Show(message, "Error");
            }
            SystemClosing = true;
            this.Close();
        }
    }
}
