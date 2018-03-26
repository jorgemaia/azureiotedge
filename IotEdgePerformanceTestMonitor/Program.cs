namespace IotEdgePerformanceTestMonitor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Runtime.Loader;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.ApplicationInsights;
    using Microsoft.ApplicationInsights.Extensibility;
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Client.Transport.Mqtt;

    class Program
    {
        // App Insights Telemetry client
        static TelemetryClient _telemetryClient;
        static Logger _logger;
        static int rawCounter; // Counter for incoming raw messagesv(from leaf devices)
        static int asaCounter; // Counter for incoming messages from ASA 

        static DateTime lastRawBatchReceived;
        static TimeSpan previousLag;
        static int _batchsize = 1000;

        static void Main(string[] args)
        {
            // If Application Insights API key was set in the env, init TelemetryClient
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY")))
            {
                _telemetryClient = new TelemetryClient();
                _logger = new Logger(_telemetryClient);
                _logger.Log("Application Insights TelemetryClient initalized");
            }
            else
            {
                _logger = new Logger();
            }

            _logger.Log("Starting IotEdgePerformanceTestMonitor module");

            // The Edge runtime gives us the connection string we need -- it is injected as an environment variable
            string connectionString = Environment.GetEnvironmentVariable("EdgeHubConnectionString");

            // Cert verification is not yet fully functional when using Windows OS for the container
            bool bypassCertVerification = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            if (!bypassCertVerification) InstallCert();
            Init(connectionString, bypassCertVerification).Wait();

            // Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
            WhenCancelled(cts.Token).Wait();
        }

        /// <summary>
        /// Handles cleanup operations when app is cancelled or unloads
        /// </summary>
        public static Task WhenCancelled(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

        /// <summary>
        /// Add certificate in local cert store for use by client for secure connection to IoT Edge runtime
        /// </summary>
        static void InstallCert()
        {
            string certPath = Environment.GetEnvironmentVariable("EdgeModuleCACertificateFile");
            if (string.IsNullOrWhiteSpace(certPath))
            {
                // We cannot proceed further without a proper cert file
                _logger.Log($"Missing path to certificate collection file: {certPath}");
                throw new InvalidOperationException("Missing path to certificate file.");
            }
            else if (!File.Exists(certPath))
            {
                // We cannot proceed further without a proper cert file
                _logger.Log($"Missing path to certificate collection file: {certPath}");
                throw new InvalidOperationException("Missing certificate file.");
            }
            X509Store store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Add(new X509Certificate2(X509Certificate2.CreateFromCertFile(certPath)));
            _logger.Log("Added Cert: " + certPath);
            store.Close();
        }


        /// <summary>
        /// Initializes the DeviceClient and sets up the callback to receive
        /// messages containing temperature information
        /// </summary>
        static async Task Init(string connectionString, bool bypassCertVerification = false)
        {
            _logger.Log("Connection String {0}", connectionString);

            MqttTransportSettings mqttSetting = new MqttTransportSettings(TransportType.Mqtt_Tcp_Only);
            // During dev you might want to bypass the cert verification. It is highly recommended to verify certs systematically in production
            if (bypassCertVerification)
            {
                mqttSetting.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
            }
            ITransportSettings[] settings = { mqttSetting };

            // Open a connection to the Edge runtime
            DeviceClient ioTHubModuleClient = DeviceClient.CreateFromConnectionString(connectionString, settings);
            await ioTHubModuleClient.OpenAsync();
            _logger.Log("IoT Hub module client initialized.");

            // Register callback to be called when a message is received by the module on input "inputRaw"
            await ioTHubModuleClient.SetInputMessageHandlerAsync("inputRaw", ProcessInputRaw, ioTHubModuleClient);

            // Register callback to be called when a message is received by the module on input "inputAsa"
            await ioTHubModuleClient.SetInputMessageHandlerAsync("inputAsa", ProcessInputAsa, ioTHubModuleClient);
        }

#pragma warning disable 1998
        /// <summary>
        /// This method is called whenever the module is sent a message from the EdgeHub. 
        /// </summary>
        static async Task<MessageResponse> ProcessInputRaw(Message message, object userContext)
        {
            int counterRawMessages = Interlocked.Increment(ref rawCounter);

            // We only print the first message and every 1000th message (or whatever the batchsize is set to)
            if (counterRawMessages == 1 || counterRawMessages % _batchsize == 0)
            {
                _logger.Log($"Raw messages recevied: {counterRawMessages}");

                if (lastRawBatchReceived == DateTime.MinValue)
                {
                    lastRawBatchReceived = DateTime.Now;
                }
                else
                {
                    var now = DateTime.Now;
                    var batchDuration = now - lastRawBatchReceived;

                    var eventsSec = Math.Round(_batchsize / batchDuration.TotalSeconds, 1);
                    _logger.Log($"{_batchsize} batch duration: {batchDuration.ToString("c")} ({eventsSec} events/sec)");

                    _telemetryClient?.TrackMetric("EventsPerSecond", eventsSec);
                    _telemetryClient?.TrackMetric("BatchDuration", batchDuration.TotalSeconds, new Dictionary<string, string> { { "BatchSize", $"{_batchsize}" } });

                    if (message.CreationTimeUtc != DateTime.MinValue)
                    {
                        var processLag = now - message.CreationTimeUtc;
                        TimeSpan lagDifference;

                        if (previousLag != TimeSpan.MinValue)
                        {
                            lagDifference = processLag - previousLag;
                        }

                        _logger.Log($"Message from device {message.ConnectionDeviceId} - Created at: {message.CreationTimeUtc.ToString("o")} - Lag: {Math.Round(processLag.TotalSeconds, 2)} sec (increased by {Math.Round(lagDifference.TotalSeconds, 2)} sec)");
                        _telemetryClient?.TrackMetric("ProcessLagSeconds", processLag.TotalSeconds);
                        previousLag = processLag;
                    }

                    lastRawBatchReceived = DateTime.Now;
                    /* 
                    var messageBytes = message.GetBytes();
                    var messageString = Encoding.UTF8.GetString(messageBytes);
                    _logger.Log($"RawInputMessageString: {messageString}");

                    foreach (var prop in message.Properties)
                    {
                        _logger.Log($"Raw Message property: {prop.Key} - {prop.Value}");
                    }
                    */
                }
            }
            return MessageResponse.Completed;
        }

        /// <summary>
        /// This method is called whenever the module is sent a message from the EdgeHub. 
        /// It just pipe the messages without any change.
        /// It prints all the incoming messages.
        /// </summary>
        static async Task<MessageResponse> ProcessInputAsa(Message message, object userContext)
        {
            int counterAsaMessages = Interlocked.Increment(ref asaCounter);

            _logger.Log($"ASA messages recevied: {counterAsaMessages}");

            var messageBytes = message.GetBytes();
            var messageString = Encoding.UTF8.GetString(messageBytes);
            _logger.Log($"AsaInputMessageString: {messageString}");

            /* 
            // ASA properties: "source=ASA", "OutputName=edgehubout", "$.on=edgehubout"
            foreach (var prop in message.Properties)
            {
                _logger.Log($"ASA Message property: {prop.Key} - {prop.Value}");
            }
            */

            return MessageResponse.Completed;
        }
#pragma warning restore 1998
    }
}
