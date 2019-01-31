using System;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;
using Serilog;

namespace helpers
{
    public static class EdgeModuleHelpers
    {
        /// <summary>
        /// Initializes the ModuleClient with AMQP as the default TransportProtocol unless otherwise set in the env var TransportProtocol
        /// Also sets a default ConnectionStatusChangesHandler
        /// </summary>
        public static async Task<ModuleClient> Init()
        {
            var transportType = TransportType.Amqp_Tcp_Only;
            string transportProtocol = Environment.GetEnvironmentVariable("TransportProtocol");

            // The way the module connects to the EdgeHub can be controlled via the env variable. Either MQTT or AMQP
            if (!string.IsNullOrEmpty(transportProtocol))
            {
                switch (transportProtocol.ToUpper())
                {
                    case "AMQP":
                        transportType = TransportType.Amqp_Tcp_Only;
                        break;
                    case "MQTT":
                        transportType = TransportType.Mqtt_Tcp_Only;
                        break;
                    default:
                        // Anything else: use default
                        Log.Warning($"Ignoring unknown TransportProtocol={transportProtocol}. Using default={transportType}");
                        break;
                }
            }

            // Open a connection to the Edge runtime
            ModuleClient moduleClient = await ModuleClient.CreateFromEnvironmentAsync(transportType);
            await moduleClient.OpenAsync();

            moduleClient.SetConnectionStatusChangesHandler(DefaultConnectionStatusHandler);

            Log.Information($"Edge Hub module client initialized using {transportType}");

            return moduleClient;
        }

        /// <summary>
        /// Callback for whenever the connection status changes
        /// Mostly we just log the new status and the reason. 
        /// But for some disconnects we need to handle them here differently for our module to recover
        /// </summary>
        /// <param name="status"></param>
        /// <param name="reason"></param>
        public static void DefaultConnectionStatusHandler(ConnectionStatus status, ConnectionStatusChangeReason reason)
        {
            Log.Information($"Module connection changed. New status={status.ToString()} Reason={reason.ToString()}");

            // Sometimes the connection can not be recovered if it is in either of those states.
            // To solve this, we exit the module. The Edge Agent will then restart it (retrying with backoff)
            if (reason == ConnectionStatusChangeReason.Retry_Expired || reason == ConnectionStatusChangeReason.Client_Close)
            {
                Log.Error($"Connection can not be re-established. Exiting module");
                Environment.Exit(1);
            }
        }

        /// <summary>
        /// Initialize logging using Serilog
        /// LogLevel can be controlled via RuntimeLogLevel env var
        /// </summary>
        public static void InitLogging()
        {
            LoggerConfiguration loggerConfiguration = new LoggerConfiguration();

            var logLevel = Environment.GetEnvironmentVariable("RuntimeLogLevel");
            logLevel = !string.IsNullOrEmpty(logLevel) ? logLevel.ToLower() : "info";

            // set the log level
            switch (logLevel)
            {
                case "fatal":
                    loggerConfiguration.MinimumLevel.Fatal();
                    break;
                case "error":
                    loggerConfiguration.MinimumLevel.Error();
                    break;
                case "warn":
                    loggerConfiguration.MinimumLevel.Warning();
                    break;
                case "info":
                case "information":
                    loggerConfiguration.MinimumLevel.Information();
                    break;
                case "debug":
                    loggerConfiguration.MinimumLevel.Debug();
                    break;
                case "verbose":
                    loggerConfiguration.MinimumLevel.Verbose();
                    break;
            }

            // set logging sinks
            loggerConfiguration.WriteTo.Console(outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] - {Message}{NewLine}{Exception}");
            loggerConfiguration.Enrich.FromLogContext();
            Log.Logger = loggerConfiguration.CreateLogger();
            Log.Information($"Initializied logger with log level {logLevel}");
        }
    }
}
