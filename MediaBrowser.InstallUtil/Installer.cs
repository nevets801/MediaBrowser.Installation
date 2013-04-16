using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ionic.Zip;
using MediaBrowser.InstallUtil.Entities;
using MediaBrowser.InstallUtil.Shortcuts;
using MediaBrowser.InstallUtil.Extensions;
using Microsoft.Win32;
using ServiceStack.Text;

namespace MediaBrowser.InstallUtil
{
    public class Installer
    {
        protected PackageVersionClass PackageClass = PackageVersionClass.Release;
        protected Version RequestedVersion = new Version(4, 0, 0, 0);
        protected Version ActualVersion;
        protected string PackageName = "MBServer";
        protected string RootSuffix = "-Server";
        protected string TargetExe = "MediaBrowser.ServerApplication.exe";
        protected string TargetArgs = "";
        protected string FriendlyName = "Media Browser Server";
        protected string Archive = null;
        protected bool InstallPismo = true;
        protected string RootPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MediaBrowser-Server");
        protected string EndInstallPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MediaBrowser-Server");
        protected IProgress<double> Progress;
        protected Action<string> ReportStatus; 

        protected bool IsUpdate = false;

        protected string TempLocation = Path.Combine(Path.GetTempPath(), "MediaBrowser");

        protected WebClient MainClient;

        public Installer(InstallationRequest request)
        {
            Init(request);
        }

        /// <summary>
        /// Initialize our internal variables from an installation request
        /// </summary>
        /// <param name="request"></param>
        protected void Init(InstallationRequest request)
        {
            IsUpdate = request.Operation == InstallOperation.Update;
            InstallPismo = request.InstallPismo;
            Archive = request.Archive;
            PackageClass = request.PackageClass;
            RequestedVersion = request.Version ?? new Version("4.0");
            Progress = request.Progress;
            ReportStatus = request.ReportStatus;
            MainClient = request.WebClient;

            switch (request.Product.ToLower())
            {
                case "mbt":
                    PackageName = "MBTheater";
                    RootSuffix = "-Theater";
                    TargetExe = "MediaBrowser.UI.exe";
                    FriendlyName = "Media Browser Theater";
                    RootPath = EndInstallPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MediaBrowser" + RootSuffix);
                    EndInstallPath = Path.Combine(RootPath, "system");
                    break;

                case "mbc":
                    PackageName = "MBClassic";
                    RootSuffix = "-WMC";
                    TargetExe = "ehshell.exe";
                    TargetArgs = @"/nostartupanimation /entrypoint:{CE32C570-4BEC-4aeb-AD1D-CF47B91DE0B2}\{FC9ABCCC-36CB-47ac-8BAB-03E8EF5F6F22}";
                    FriendlyName = "Media Browser Classic";
                    RootPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MediaBrowser" + RootSuffix);
                    EndInstallPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "ehome");
                    break;

                default:
                    PackageName = "MBServer";
                    RootSuffix = "-Server";
                    TargetExe = "MediaBrowser.ServerApplication.exe";
                    FriendlyName = "Media Browser Server";
                    RootPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MediaBrowser" + RootSuffix);
                    EndInstallPath = Path.Combine(RootPath, "system");
                    break;
            }

        }

        /// <summary>
        /// Parse an argument string array into an installation request and wait on a calling process if there was one
        /// </summary>
        /// <param name="argString"></param>
        /// <returns></returns>
        public static InstallationRequest ParseArgsAndWait(string[] argString)
        {
            var request = new InstallationRequest();

            var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in argString)
            {
                var nameValue = pair.Split('=');
                if (nameValue.Length == 2)
                {
                    args[nameValue[0]] = nameValue[1];
                }
            }
            request.Archive = args.GetValueOrDefault("archive", null);
            if (args.GetValueOrDefault("pismo", "true") == "false") request.InstallPismo = false;

            request.Product = args.GetValueOrDefault("product", null) ?? ConfigurationManager.AppSettings["product"] ?? "server";
            request.PackageClass = (PackageVersionClass)Enum.Parse(typeof(PackageVersionClass), args.GetValueOrDefault("class", null) ?? ConfigurationManager.AppSettings["class"] ?? "Release");
            request.Version = new Version(args.GetValueOrDefault("version", "4.0"));

            var callerId = args.GetValueOrDefault("caller", null);
            if (callerId != null)
            {
                // Wait for our caller to exit
                try
                {
                    var process = Process.GetProcessById(Convert.ToInt32(callerId));
                    process.WaitForExit();
                }
                catch (ArgumentException)
                {
                    // wasn't running
                }

                request.Operation = InstallOperation.Update;
            }
            else
            {
                request.Operation = InstallOperation.Install;
            }

            return request;
        }

        /// <summary>
        /// Execute the install process
        /// </summary>
        /// <returns></returns>
        public async Task<InstallationResult> DoInstall(string archive)
        {
            ReportStatus(String.Format("Installing {0}...", FriendlyName));

            // Determine Package version
            var version = archive == null ? await GetPackageVersion() : null;
            ActualVersion = version != null ? version.version : new Version(3, 0);

            // Now try and shut down the server if that is what we are installing and it is running
            var procs = Process.GetProcessesByName("mediabrowser.serverapplication");
            var server = procs.Length > 0 ? procs[0] : null;
            if (!IsUpdate && PackageName == "MBServer" && server != null)
            {
                ReportStatus("Shutting Down Media Browser Server...");
                using (var client = new WebClient())
                {
                    try
                    {
                        client.UploadString("http://localhost:8096/mediabrowser/System/Shutdown", "");
                        try
                        {
                            server.WaitForExit(30000); //don't hang indefinitely
                        }
                        catch (ArgumentException)
                        {
                            // already gone
                        }
                    }
                    catch (WebException e)
                    {
                        if (e.Status != WebExceptionStatus.Timeout && !e.Message.StartsWith("Unable to connect", StringComparison.OrdinalIgnoreCase))
                        {
                            return new InstallationResult(false, "Error shutting down server. Please be sure it is not running and try again.", e);
                        }
                    }
                }
            }
            else
            {
                if (!IsUpdate && PackageName == "MBTheater")
                {
                    // Uninstalling MBT - shut it down if it is running
                    var processes = Process.GetProcessesByName("mediabrowser.ui");
                    if (processes.Length > 0)
                    {
                        ReportStatus("Shutting Down Media Browser Theater...");
                        try
                        {
                            processes[0].Kill();
                        }
                        catch (Exception ex)
                        {
                            return new InstallationResult(false, "Unable to shutdown Media Browser Theater.  Please ensure it is not running before hitting OK.",ex);
                        }
                    }
                }
            }

            // Download if we don't already have it
            if (archive == null)
            {
                ReportStatus(String.Format("Downloading {0} (version {1})...", FriendlyName, ActualVersion));
                try
                {
                    archive = await DownloadPackage(version);
                }
                catch (Exception e)
                {
                    return new InstallationResult(false, "Error Downloading Package", e);
                }
            }

            if (archive == null) return new InstallationResult(false);  //we canceled or had an error that was already reported

            // Create our main directory and set permissions - this should only happen on install
            if (!IsUpdate && !Directory.Exists(RootPath))
            {
                ReportStatus("Setting access rights.  This may take a minute...");
                var info = Directory.CreateDirectory(RootPath);
                await SetPermissions(info);
            }

            if (Path.GetExtension(archive) == ".msi")
            {

                var logPath = Path.Combine(RootPath, "Logs");
                if (!Directory.Exists(logPath)) Directory.CreateDirectory(logPath);

                // Run in silent mode and wait for it to finish
                // First uninstall any previous version
                ReportStatus("Uninstalling any previous version...");
                var logfile = Path.Combine(RootPath, "logs", "UnInstall.log");
                var uninstaller = Process.Start("msiexec", "/x \"" + archive + "\" /quiet /le \"" + logfile + "\"");
                if (uninstaller != null) uninstaller.WaitForExit();
                // And now installer
                ReportStatus("Installing " + FriendlyName);
                logfile = Path.Combine(RootPath, "logs", "Install.log");
                var installer = Process.Start(archive, "/quiet /le \"" + logfile + "\"");
                installer.WaitForExit();  // let this throw if there is a problem
            }
            else
            {
                // Extract
                ReportStatus("Extracting Package...");
                var retryCount = 0;
                var success = false;
                while (!success && retryCount < 3)
                {
                    var result = await ExtractPackage(archive);

                    if (!result.Success)
                    {
                        if (retryCount < 3)
                        {
                            retryCount++;
                            Thread.Sleep(500);
                        }
                        else
                        {
                            // Delete archive even if failed so we don't try again with this one
                            TryDelete(archive);
                            return result;
                        }
                    }
                    else
                    {
                        success = true;
                        // We're done with it so delete it (this is necessary for update operations)
                        TryDelete(archive);
                        // Also be sure there isn't an old update lying around
                        if (!IsUpdate) RemovePath(Path.Combine(RootPath, "Updates"));
                    }
                }

                // Create shortcut
                ReportStatus("Creating Shortcuts...");
                var fullPath = Path.Combine(RootPath, "System", TargetExe);
                try
                {
                    var result = CreateShortcuts(fullPath);
                    if (!result.Success) return result;
                }
                catch (Exception e)
                {
                    return new InstallationResult(false, "Error Creating Shortcut", e);
                }

                // Install Pismo
                if (InstallPismo)
                {
                    ReportStatus("Installing ISO Support...");
                    try
                    {
                        PismoInstall();
                    }
                    catch (Exception e)
                    {
                        return new InstallationResult(false, "Error Installing ISO support", e);
                    }
                }

                // Now delete the pismo install files
                Directory.Delete(Path.Combine(RootPath, "Pismo"), true);


            }

            // And run
            ReportStatus(String.Format("Starting {0}...", FriendlyName));
            try
            {
                Process.Start(Path.Combine(EndInstallPath, TargetExe), TargetArgs);
            }
            catch (Exception e)
            {
                return new InstallationResult(false, "Error Executing - " + Path.Combine(EndInstallPath, TargetExe) + " " + TargetArgs, e);
            }

            return new InstallationResult();

        }

        /// <summary>
        /// Set permissions for all users
        /// </summary>
        /// <param name="directoryInfo"></param>
        private Task SetPermissions(DirectoryInfo directoryInfo)
        {
            return Task.Run(() =>
            {
                var securityIdentifier = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

                var directorySecurity = directoryInfo.GetAccessControl();
                var rule = new FileSystemAccessRule(
                        securityIdentifier,
                        FileSystemRights.Write |
                        FileSystemRights.ReadAndExecute |
                        FileSystemRights.Modify,
                        AccessControlType.Allow);
                bool modified;

                directorySecurity.ModifyAccessRule(AccessControlModification.Add, rule, out modified);
                directoryInfo.SetAccessControl(directorySecurity);
                
            });

        }

        private void PismoInstall()
        {
            // Kick off the Pismo installer and wait for it to end
            var pismo = new Process {StartInfo = {WindowStyle = ProcessWindowStyle.Hidden, FileName = Path.Combine(RootPath, "Pismo", "pfminst.exe"), Arguments = "install"}};
            pismo.Start();
            pismo.WaitForExit();

        }

        protected async Task<PackageVersionInfo> GetPackageVersion()
        {
            // get the package information for the server
            try
            {
                var json = await MainClient.DownloadStringTaskAsync("http://www.mb3admin.com/admin/service/package/retrieveAll?name=" + PackageName);
                var packages = JsonSerializer.DeserializeFromString<List<PackageInfo>>(json);

                return packages[0].versions.Where(v => v.classification <= PackageClass).OrderByDescending(v => v.version).FirstOrDefault(v => v.version <= RequestedVersion);
            }
            catch (Exception e)
            {
                return null;
            }
        }

        /// <summary>
        /// Download our specified package to an archive in a temp location
        /// </summary>
        /// <returns>The fully qualified name of the downloaded package</returns>
        protected async Task<string> DownloadPackage(PackageVersionInfo version)
        {
            var success = false;
            var retryCount = 0;
            var archiveFile = Path.Combine(PrepareTempLocation(), version.targetFilename);

            while (!success && retryCount < 3)
            {

                // setup download progress and download the package
                MainClient.DownloadProgressChanged += DownloadProgressChanged;
                try
                {
                    await MainClient.DownloadFileTaskAsync(version.sourceUrl, archiveFile);
                    success = true;
                }
                catch (WebException e)
                {
                    if (e.Status == WebExceptionStatus.RequestCanceled)
                    {
                        return null;
                    }
                    if (retryCount < 3 && (e.Status == WebExceptionStatus.Timeout || e.Status == WebExceptionStatus.ConnectFailure || e.Status == WebExceptionStatus.ProtocolError))
                    {
                        Thread.Sleep(500); //wait just a sec
                        PrepareTempLocation(); //clear this out
                        retryCount++;
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            return archiveFile;
        }

        void DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            Progress.Report(e.ProgressPercentage);
        }

        /// <summary>
        /// Extract the provided archive to our program root
        /// It is assumed the archive is a zip file relative to that root (with all necessary sub-folders)
        /// </summary>
        /// <param name="archive"></param>
        protected Task<InstallationResult> ExtractPackage(string archive)
        {
            return Task.Run(() =>
                                {
                                    // Delete old content of system
                                    var systemDir = Path.Combine(RootPath, "System");
                                    var backupDir = Path.Combine(RootPath, "System.old");
                                    if (Directory.Exists(systemDir))
                                    {
                                        try
                                        {
                                            if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true);

                                        }
                                        catch (Exception e)
                                        {
                                            return new InstallationResult(false, "Could not delete previous backup directory.", e);
                                        }

                                        try
                                        {
                                            Directory.Move(systemDir, backupDir);
                                        }
                                        catch (Exception e)
                                        {
                                            return new InstallationResult(false, "Could not move system directory to backup.", e);
                                        }
                                    }

                                    // And extract
                                    var retryCount = 0;
                                    var success = false;
                                    while (!success && retryCount < 3)
                                    {
                                        try
                                        {
                                            using (var fileStream = File.OpenRead(archive))
                                            {
                                                using (var zipFile = ZipFile.Read(fileStream))
                                                {
                                                    zipFile.ExtractAll(RootPath, ExtractExistingFileAction.OverwriteSilently);
                                                    success = true;
                                                }
                                            }
                                        }
                                        catch (Exception e)
                                        {
                                            if (retryCount < 3)
                                            {
                                                Thread.Sleep(250);
                                                retryCount++;
                                            }
                                            else
                                            {
                                                //Rollback
                                                RollBack(systemDir, backupDir);
                                                return new InstallationResult(false, String.Format("Could not extract {0} to {1} after {2} attempts.", archive, RootPath, retryCount), e);
                                            }
                                        }
                                    }

                                    return new InstallationResult();

                                });
        }

        protected void RollBack(string systemDir, string backupDir)
        {
            if (Directory.Exists(backupDir))
            {
                if (Directory.Exists(systemDir)) Directory.Delete(systemDir);
                Directory.Move(backupDir, systemDir);
            }
        }

        /// <summary>
        /// Create a shortcut in the current user's start menu
        ///  Only do current user to avoid need for admin elevation
        /// </summary>
        /// <param name="targetExe"></param>
        protected InstallationResult CreateShortcuts(string targetExe)
        {
            // get path to all users start menu
            var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Media Browser 3");
            if (!Directory.Exists(startMenu)) Directory.CreateDirectory(startMenu);
            var product = new ShellShortcut(Path.Combine(startMenu, FriendlyName + ".lnk")) { Path = targetExe, Description = "Run " + FriendlyName };
            product.Save();

            if (PackageName == "MBServer")
            {
                var path = Path.Combine(startMenu, "MB Dashboard.lnk");
                var dashboard = new ShellShortcut(path) { Path = @"http://localhost:8096/mediabrowser/dashboard/dashboard.html", Description = "Open the Media Browser Server Dashboard (configuration)" };
                dashboard.Save();

            }

            return CreateUninstaller(Path.Combine(Path.GetDirectoryName(targetExe) ?? "", "MediaBrowser.Uninstaller.exe") + " " + (PackageName == "MBServer" ? "server" : "mbt"), targetExe);

        }

        /// <summary>
        /// Create uninstall entry in add/remove
        /// </summary>
        /// <param name="uninstallPath"></param>
        /// <param name="targetExe"></param>
        private InstallationResult CreateUninstaller(string uninstallPath, string targetExe)
        {
            var parent = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", true);
            {
                if (parent == null)
                {
                    var rootParent = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion", true);
                    {
                        if (rootParent != null)
                        {
                            parent = rootParent.CreateSubKey("Uninstall");
                            if (parent == null)
                            {
                                return new InstallationResult(false, "Unable to create Uninstall registry key.  Program is still installed sucessfully.");
                            }
                        }
                    }
                }
                try
                {
                    RegistryKey key = null;

                    try
                    {
                        const string guidText = "{4E76DB4E-1BB9-4A7B-860C-7940779CF7A0}";
                        key = parent.OpenSubKey(guidText, true) ??
                              parent.CreateSubKey(guidText);

                        if (key == null)
                        {
                            return new InstallationResult(false, String.Format("Unable to create uninstaller entry'{0}\\{1}'.  Program is still installed successfully.", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", guidText));
                        }

                        key.SetValue("DisplayName", FriendlyName);
                        key.SetValue("ApplicationVersion", ActualVersion);
                        key.SetValue("Publisher", "Media Browser Team");
                        key.SetValue("DisplayIcon", targetExe);
                        key.SetValue("DisplayVersion", ActualVersion.ToString(2));
                        key.SetValue("URLInfoAbout", "http://www.mediabrowser3.com");
                        key.SetValue("Contact", "http://community.mediabrowser.tv");
                        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                        key.SetValue("UninstallString", uninstallPath);
                    }
                    finally
                    {
                        if (key != null)
                        {
                            key.Close();
                        }
                    }
                }
                catch (Exception ex)
                {
                    return new InstallationResult(false, "An error occurred writing uninstall information to the registry.", ex);
                }
            }
            
            return new InstallationResult();
        }

        /// <summary>
        /// Prepare a temporary location to download to
        /// </summary>
        /// <returns>The path to the temporary location</returns>
        protected string PrepareTempLocation()
        {
            ClearTempLocation(TempLocation);
            Directory.CreateDirectory(TempLocation);
            return TempLocation;
        }

        /// <summary>
        /// Publicly accessible version to clear our temp location
        /// </summary>
        public void ClearTempLocation()
        {
            ClearTempLocation(TempLocation);
        }

        /// <summary>
        /// Clear out (delete recursively) the supplied temp location
        /// </summary>
        /// <param name="location"></param>
        protected void ClearTempLocation(string location)
        {
            if (Directory.Exists(location))
            {
                Directory.Delete(location, true);
            }
        }

        private static void RemovePath(string path)
        {
            try
            {
                Directory.Delete(path, true);
            }
            catch (DirectoryNotFoundException)
            {
            }

        }

        private bool TryDelete(string file)
        {
            try
            {
                File.Delete(file);
            }
            catch (FileNotFoundException)
            {
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }
    }
}
