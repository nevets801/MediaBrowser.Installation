using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using MediaBrowser.InstallUtil;

namespace MediaBrowser.Updater
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
            InitializeComponent();
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MediaBrowser-InstallLogs");
            if (!Directory.Exists(logPath)) Directory.CreateDirectory(logPath);
            var request = InstallUtil.Installer.ParseArgsAndWait(Environment.GetCommandLineArgs());
            var logFile = Path.Combine(logPath, request.Product + "-update.log");
            if (File.Exists(logFile)) File.Delete(logFile);
            Trace.Listeners.Add(new TextWriterTraceListener(logFile));
            Trace.AutoFlush = true;
            request.ReportStatus = UpdateStatus;
            request.Progress = new ProgressUpdater(this);
            request.WebClient = MainClient;
            Trace.TraceInformation("Creating update session for {0}", request.Product);
            DoUpdate(new Installer(request));  // fire and forget so we get our window up

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

        private async Task DoUpdate(Installer installer)
        {
            var result = await installer.DoUpdate();
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
            Installer.ClearTempLocation();
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
