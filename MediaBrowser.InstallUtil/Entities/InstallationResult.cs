using System;

namespace MediaBrowser.InstallUtil.Entities
{
    public class InstallationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public Exception Exception { get; set; }

        public InstallationResult(bool success = true, string message = "", Exception exception = null)
        {
            Success = success;
            Message = message;
            Exception = exception;
        }
    }
}
