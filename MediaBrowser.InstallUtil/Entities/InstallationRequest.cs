﻿using System;

namespace MediaBrowser.InstallUtil.Entities
{
    public class InstallationRequest
    {
        public InstallOperation Operation { get; set; }
        public string Archive { get; set; }
        public string Product { get; set; }
        public PackageVersionClass PackageClass { get; set; }
        public Version Version { get; set; }
        public bool InstallPismo { get; set; }
        public IProgress<long> Progress { get; set; }
        public Action<string> ReportStatus { get; set; } 
    }
}
