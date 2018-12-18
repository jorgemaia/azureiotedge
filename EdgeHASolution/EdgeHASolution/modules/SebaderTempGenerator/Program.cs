namespace SebaderTempGenerator
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Runtime.Loader;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Client;
    using Newtonsoft.Json;

    class Program
    {
        static int counter;

        static void Main()
        {
            var moduleClient = Init().Result;

            // Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();

            SendMessagesForever(moduleClient, cts.Token).Wait();

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
        /// Initializes the ModuleClient and sets up the callback to receive
        /// messages containing temperature information
        /// </summary>
        static async Task<ModuleClient> Init()
        {
            // Open a connection to the Edge runtime
            ModuleClient ioTHubModuleClient = await ModuleClient.CreateFromEnvironmentAsync(TransportType.Mqtt_Tcp_Only).ConfigureAwait(false);
            await ioTHubModuleClient.OpenAsync().ConfigureAwait(false);
            Console.WriteLine("IoT Hub module client initialized.");

           return ioTHubModuleClient;
        }

        /// <summary>
        /// This method is called whenever the module is sent a message from the EdgeHub. 
        /// It just pipe the messages without any change.
        /// It prints all the incoming messages.
        /// </summary>
        static async Task SendMessagesForever(ModuleClient moduleClient, CancellationToken cancellationToken)
        {
            Random rand = new Random();

            while (true)
            {
                int counterValue = Interlocked.Increment(ref counter);

                
                dynamic message = new {
                    temperature = rand.NextDouble() * 20,
                    pressure = rand.Next(0, 10),
                    machine = "SimTempSensor",
                    timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
                };

                string json = JsonConvert.SerializeObject(message);

                Console.WriteLine($"Sending message: {counterValue}, Body: [{json}]");

                var messageBytes = Encoding.UTF8.GetBytes(json);
                var pipeMessage = new Message(messageBytes);

                await moduleClient.SendEventAsync("output1", pipeMessage).ConfigureAwait(false);
                Console.WriteLine("Message sent");

                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
