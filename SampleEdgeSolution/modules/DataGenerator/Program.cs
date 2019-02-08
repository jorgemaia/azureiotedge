namespace DataGenerator
{
    using System;
    using System.Runtime.Loader;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Shared;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Serilog;


    /// <summary>
    /// Sample Azure IoT Edge module that generates random data and sends it to it's output
    /// Runs indefinetly
    /// Generates temperature and humidity readings as slowly changing values. So no random jumping of the values.
    /// Creates nice graphs over time
    /// </summary>
    class Program
    {
        private static int _counter;
        private static Random _rand = new Random();

        private static int _samplingRateMs = 1000;

        private static TwinCollection _reportedProperties = new TwinCollection();

        public static int Main() => MainAsync().Result;

        static async Task<int> MainAsync()
        {
            InitLogging();
            Log.Information($"Module {Environment.GetEnvironmentVariable("IOTEDGE_MODULEID")} starting up...");
            var moduleClient = await Init();

            // Get initial device twin
            var twin = await moduleClient.GetTwinAsync();
            // Process desired properties
            await ProcessDesiredPropertiesUpdate(twin.Properties.Desired, moduleClient);

            // Set twin properties updated handler
            await moduleClient.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertyChanged, moduleClient);

            // Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();

            // Start generating and sending messages
            await SendMessagesForever(moduleClient, cts.Token);

            await WhenCancelled(cts.Token);
            return 0;
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
        /// Initializes the ModuleClient
        /// </summary>
        static async Task<ModuleClient> Init()
        {
            var transportType = TransportType.Amqp_Tcp_Only;
            string transportProtocol = Environment.GetEnvironmentVariable("ClientTransportType");

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
            moduleClient.SetConnectionStatusChangesHandler(ConnectionStatusHandler);

            await moduleClient.OpenAsync();
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
        private static void ConnectionStatusHandler(ConnectionStatus status, ConnectionStatusChangeReason reason)
        {
            Log.Information($"Module connection changed. New status={status.ToString()} Reason={reason.ToString()}");

            // Sometimes the connection can not be recovered if it is in either of those states.
            // To solve this, we exit the module. The Edge Agent will then restart it (retrying with backoff)
            if (reason == ConnectionStatusChangeReason.Retry_Expired)
            {
                Log.Error($"Connection can not be re-established. Exiting module");
                Environment.Exit(1);
            }
        }

        /// <summary>
        /// Initialize logging using Serilog
        /// LogLevel can be controlled via RuntimeLogLevel env var
        /// </summary>
        private static void InitLogging()
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

        /// <summary>
        ///     Event handler for incoming device twin updates
        /// </summary>
        /// <param name="desiredProperties"></param>
        /// <param name="userContext"></param>
        /// <returns></returns>
        private static async Task OnDesiredPropertyChanged(TwinCollection desiredProperties, object userContext)
        {
            Log.Information($"Desired property change received:");
            Log.Debug(JsonConvert.SerializeObject(desiredProperties));
            await ProcessDesiredPropertiesUpdate(desiredProperties, (ModuleClient)userContext);
        }

        /// <summary>
        /// Process a collection of desired device twin properties
        /// Sends back the reported properties
        /// </summary>
        /// <param name="desiredProperties"></param>
        /// <returns></returns>
        private static async Task ProcessDesiredPropertiesUpdate(TwinCollection desiredProperties, ModuleClient moduleClient)
        {
            if (desiredProperties.Contains("samplingrate") &&
                desiredProperties["samplingrate"].Type == JTokenType.Integer)
            {
                int desiredSamplingRate = desiredProperties["samplingrate"];
                // only allow values between 1ms and 60000ms (1min)
                if (desiredSamplingRate >= 1 && desiredSamplingRate <= 60000)
                {
                    Log.Information($"Setting samplingRate to {desiredSamplingRate}ms");
                    _samplingRateMs = desiredSamplingRate;
                }
            }

            _reportedProperties["samplingrate"] = _samplingRateMs;

            // Report properties back to IoT hub
            await moduleClient.UpdateReportedPropertiesAsync(_reportedProperties);
        }

        /// <summary>
        /// This method is called whenever the module is sent a message from the EdgeHub. 
        /// It just pipe the messages without any change.
        /// It prints all the incoming messages.
        /// </summary>
        static async Task SendMessagesForever(ModuleClient moduleClient, CancellationToken cancellationToken)
        {
            // Read ModuleId from env
            string moduleId = Environment.GetEnvironmentVariable("IOTEDGE_MODULEID");

            while (!cancellationToken.IsCancellationRequested)
            {
                int counterValue = Interlocked.Increment(ref _counter);

                var telemetryDataPoint = new
                {
                    deviceId = moduleId,
                    deviceTime = DateTime.UtcNow.ToString("o"),
                    temperature = GetNewTemperature(), // Calculates new temperature reading
                    humidity = GetNewHumidity() // Calculates new humidity reading
                };

                string json = JsonConvert.SerializeObject(telemetryDataPoint);

                var messageBytes = Encoding.UTF8.GetBytes(json);
                var outMessage = new Message(messageBytes);
                outMessage.ContentType = "application/json";
                outMessage.ContentEncoding = "UTF-8";

                Log.Information($"Sending message: Count: {counterValue}, Body: [{json}]");
                await moduleClient.SendEventAsync("output1", outMessage);
                Log.Debug($"Message {counterValue} sent");

                // Sleep for duration of samplingRate
                await Task.Delay(_samplingRateMs, cancellationToken);
            }
        }

        private static double LastTemperature { get; set; }

        /// <summary>
        /// Calling GetNewTemperature will trigger a recalculation of the sensor reading and write the new value to LastTemperature
        /// </summary>
        private static double GetNewTemperature()
        {

            double currentTemperature = LastTemperature;

            // We built a random device failure / anomaly in here. In ~0.2% of all readings, 
            // the temperature reading drops or increases in an instant by +/-20°
            if (_rand.Next(0, 500) == 0)
            {
                Log.Information("Random event: Major temperature shift +/- 20 degrees C");
                // Randomly add or subtract 20 degrees to the temperatur
                currentTemperature += _rand.Next(2) == 0 ? 20 : -20;
            }
            else
            {
                // Random based on lastTemp. Only increase/decrease by max 0.5 degree. This way we dont generate too high jumps.
                currentTemperature += NextDouble(-0.5, 0.5);
            }

            // We dont want the temp to grow out of bounds
            if (currentTemperature < -20)
                currentTemperature = -20;
            else if (currentTemperature > 45)
                currentTemperature = 45;

            LastTemperature = currentTemperature;
            return currentTemperature;
        }

        private static int LastHumidity { get; set; }

        /// <summary>
        /// Calling GetNewHumidity will trigger a recalculation of the sensor reading and write the new value to LastHumidity
        /// </summary>
        private static int GetNewHumidity()
        {
            int lastHumidity = LastHumidity;

            // We built a loose correlation between Temperatur and Humidity:
            // Colder temp = higher Humidity. Does it make sense? Who knows...
            if (LastTemperature < 5 && lastHumidity < 70)
                lastHumidity = 80;
            else if (LastTemperature >= 5 && LastTemperature < 25 && lastHumidity < 50)
                lastHumidity = 60;
            else if (LastTemperature >= 25 && lastHumidity > 40)
                lastHumidity = 30;

            int humidity = lastHumidity + _rand.Next(-10, +11);

            // Humidity cannot be < 0 and > 100 
            if (humidity < 0)
                humidity = _rand.Next(0, 20);
            else if (humidity > 100)
                humidity = _rand.Next(90, 101);

            LastHumidity = humidity;
            return humidity;
        }

        /// <summary>
        /// Generates a random double between two boundries
        /// </summary>
        /// <param name="MinValue"></param>
        /// <param name="MaxValue"></param>
        /// <returns></returns>
        private static double NextDouble(double MinValue, double MaxValue) => _rand.NextDouble() * Math.Abs(MaxValue - MinValue) + MinValue;
    }
}