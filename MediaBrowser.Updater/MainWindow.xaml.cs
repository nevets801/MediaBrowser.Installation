using System;
using System.Net;
using System.Threading.Tasks;
using System.Windows;

namespace MediaBrowser.Updater
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        protected bool SystemClosing = false;

        protected WebClient MainClient = new WebClient();

        protected InstallUtil.Installer Installer;

        public MainWindow()
        {
            InitializeComponent();
            var request = InstallUtil.Installer.ParseArgsAndWait(Environment.GetCommandLineArgs());
            request.ReportStatus = UpdateStatus;
            request.Progress = new ProgressUpdater(this);
            request.WebClient = MainClient;
            Installer = new InstallUtil.Installer(request);
            DoUpdate(request.Archive);  // fire and forget so we get our window up

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

        private async Task DoUpdate(string archive)
        {
            var result = await Installer.DoUpdate(archive);
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
