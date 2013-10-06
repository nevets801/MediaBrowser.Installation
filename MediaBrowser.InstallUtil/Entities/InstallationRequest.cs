using System;
using System.Net;

namespace MediaBrowser.InstallUtil.Entities
{
    public class InstallationRequest
    {
        public InstallOperation Operation { get; set; }
        public string Archive { get; set; }
        public string Product { get; set; }
        public PackageVersionClass PackageClass { get; set; }
        public Version Version { get; set; }
        public IProgress<double> Progress { get; set; }
        public Action<string> ReportStatus { get; set; }
        public WebClient WebClient { get; set; }
        public string ServiceName { get; set; }
    }
}
