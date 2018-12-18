using System;
using System.Runtime.CompilerServices;
using Microsoft.ApplicationInsights;

namespace SqlConnectionModule
{
    public class Logger
    {
        private TelemetryClient telemetryClient { get; set; }
        
        private string LoggerPrefix { get; set; }

        // public bool IsEnabled {get; set;}

        public Logger()
        {
            this.LoggerPrefix = "";
        }

        public Logger(TelemetryClient telemetryClient)
        {
            this.telemetryClient = telemetryClient;
            this.LoggerPrefix = "";
        }

        public Logger(string prefix, TelemetryClient telemetryClient)
        {
            this.telemetryClient = telemetryClient;
            this.LoggerPrefix = prefix;
        }

        public Logger(string prefix)
        {
            this.LoggerPrefix = prefix;
        }

        public void Log(string message, [CallerMemberName] string callingMethod = "")
        {
            string output = $"{DateTime.Now.ToString("o")}";
            if (LoggerPrefix.Length > 0)
            {
                output += " / " + LoggerPrefix;
            }
            output += $" / {callingMethod} / {message}";
            Console.WriteLine(output);

            // If Application Insights is configured, also log to there
            if (telemetryClient != null)
            {
                telemetryClient.TrackTrace(message);
            }
        }

        public void Log(Exception exc, [CallerMemberName] string callingMethod = "")
        {
            string output = exc.Message + Environment.NewLine + exc.StackTrace;
            Log(output, callingMethod);
        }
    }
}