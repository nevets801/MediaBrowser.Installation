using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using MediaBrowser.InstallUtil;

namespace MediaBrowser.Updater.Console
{
    class Program
    {
        protected static WebClient MainClient = new WebClient();

        static void Main(string[] args)
        {
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MediaBrowser-InstallLogs");
            if (!Directory.Exists(logPath)) Directory.CreateDirectory(logPath);
            var request = Installer.ParseArgsAndWait(args);
            var logFile = Path.Combine(logPath, request.Product + "-update.log");
            if (File.Exists(logFile)) File.Delete(logFile);
            Trace.Listeners.Add(new TextWriterTraceListener(logFile));
            Trace.AutoFlush = true;
            request.ReportStatus = UpdateStatus;
            request.Progress = new Progress<double>();
            request.WebClient = MainClient;
            Trace.TraceInformation("Creating update session for {0}", request.Product);
            Task.WaitAll(DoUpdate(new Installer(request)));
        }

        private static async Task DoUpdate(Installer installer)
        {
            await installer.DoUpdate();
        }

        private static void UpdateStatus(string msg)
        {
            // no status updates - utility will log
        }
    }
}
