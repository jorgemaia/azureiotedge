namespace SqlConnectionModule
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
    using Sql = System.Data.SqlClient;
    using Newtonsoft.Json;
    using Microsoft.ApplicationInsights;
    using Microsoft.ApplicationInsights.Extensibility;
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Client.Transport.Mqtt;

    class Program
    {
        // App Insights Telemetry client
        static TelemetryClient _telemetryClient;
        static Logger _logger;

        static int counterRawMessages;
        static int counterAsaMessages;
        private static string _sqlConnString;

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

            // The Edge runtime gives us the connection string we need -- it is injected as an environment variable
            string connectionString = Environment.GetEnvironmentVariable("EdgeHubConnectionString");

            // Read SQL Connection string from env
            _sqlConnString = Environment.GetEnvironmentVariable("SQLConnectionString");

            // Cert verification is not yet fully functional when using Windows OS for the container
            bool bypassCertVerification = true; // RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
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

        /// <summary>
        /// This method is called whenever the module is sent a message from the EdgeHub. 
        /// It just pipe the messages without any change.
        /// It prints all the incoming messages.
        /// </summary>
        static async Task<MessageResponse> ProcessInputRaw(Message message, object userContext)
        {
            int counterValue = Interlocked.Increment(ref counterRawMessages);

            byte[] messageBytes = message.GetBytes();
            string messageString = Encoding.UTF8.GetString(messageBytes);
            _logger.Log($"Received raw message: {counterValue}");

            if (!string.IsNullOrEmpty(messageString))
            {
                var eventList = JsonConvert.DeserializeObject<List<MessageBodyRawInput>>(messageString);

                string insertRowStatement = "";
                foreach (MessageBodyRawInput item in eventList)
                {
                    insertRowStatement += $"INSERT INTO MeasurementsDB.dbo.TemperatureHumidity VALUES (CONVERT(DATETIME2,'{item.deviceTime}', 127), '{item.deviceId}', {item.temperatur}, {item.humidity});\n";
                }

                //Store the data in SQL db
                using (Sql.SqlConnection conn = new Sql.SqlConnection(_sqlConnString))
                {
                    conn.Open();
                    using (Sql.SqlCommand cmd = new Sql.SqlCommand(insertRowStatement, conn))
                    {
                        //Execute the command and log the # rows affected.
                        var rows = await cmd.ExecuteNonQueryAsync();
                        _logger.Log($"{rows} rows were inserted into dbo.TemperatureHumidity");
                    }
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
            int counterValue = Interlocked.Increment(ref counterAsaMessages);

            byte[] messageBytes = message.GetBytes();
            string messageString = Encoding.UTF8.GetString(messageBytes);
            _logger.Log($"Received ASA message: {counterValue}");

            if (!string.IsNullOrEmpty(messageString))
            {
                var eventList = JsonConvert.DeserializeObject<List<MessageBodyAsaInput>>(messageString);

                string insertRowStatement = "";
                foreach (MessageBodyAsaInput item in eventList)
                {
                    insertRowStatement += $"INSERT INTO MeasurementsDB.dbo.AggreatedMeasurements (deviceId, timestamp, avgHumidity, avgTemperature) VALUES ({item.deviceId}', CONVERT(DATETIME2,'{item.WindowEndTime}', 127), {item.avgHumidity}, {item.avgTemperature});\n";
                }

                //Store the data in SQL db
                using (Sql.SqlConnection conn = new Sql.SqlConnection(_sqlConnString))
                {
                    conn.Open();
                    using (Sql.SqlCommand cmd = new Sql.SqlCommand(insertRowStatement, conn))
                    {
                        //Execute the command and log the # rows affected.
                        var rows = await cmd.ExecuteNonQueryAsync();
                        _logger.Log($"{rows} rows were inserted");
                    }
                }
            }
            return MessageResponse.Completed;
        }
    }

    class MessageBodyRawInput
    {
        public string deviceId { get; set; }
        public string eventId { get; set; }
        public string temperatur { get; set; }
        public string humidity { get; set; }
        public string deviceTime { get; set; }
    }
    class MessageBodyAsaInput
    {
        public string deviceId { get; set; }
        public string WindowEndTime { get; set; }
        public string avgTemperature { get; set; }
        public string avgHumidity { get; set; }
    }
}
