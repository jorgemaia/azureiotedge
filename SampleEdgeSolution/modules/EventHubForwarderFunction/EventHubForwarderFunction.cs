namespace Functions.Samples
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.EventHubs;
    using Microsoft.Azure.WebJobs;
    using Microsoft.Azure.WebJobs.Extensions.EdgeHub;
    using Microsoft.Azure.WebJobs.Host;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;

    /// <summary>
    /// This is a sample of a C# Azure Function for IoT Edge that takes in events sent by the EdgeHub and sends them to an external Azure Event Hub
    /// </summary>
    public static class EventHubForwarderFunction
    {
        private static EventHubClient eventHubClient;

        [FunctionName("EventHubForwarder")]
        public static async Task SendMessageToEventHub([EdgeHubTrigger("input")] Message messageReceived, ILogger logger)
        {
            if (eventHubClient == null)
            {
                eventHubClient = InitEventHubClient(logger);
            }

            byte[] messageBytes = messageReceived.GetBytes();

            if (messageBytes != null && messageBytes.Length > 0)
            {
                logger.LogDebug($"Received message: {System.Text.Encoding.UTF8.GetString(messageBytes)}");

                await eventHubClient.SendAsync(new EventData(messageBytes));
                logger.LogInformation("Piped out a message to EventHub");
            }
            else
            {
                logger.LogInformation("Received message with empty payload. Ignoring");
            }
        }

        /// <summary>
        /// Init EventHub client based on env variables
        /// </summary>
        /// <param name="logger"></param>
        /// <returns></returns>
        private static EventHubClient InitEventHubClient(ILogger logger)
        {
            var eventHubConnectionString = Environment.GetEnvironmentVariable("EventHubConnectionString");
            if (string.IsNullOrEmpty(eventHubConnectionString))
            {
                throw new Exception("EventHubConnectionString environment variable not set. Cannot execute Function");
            }

            var ehUpstreamProtocol = Environment.GetEnvironmentVariable("EventHubUpstreamProtocol");

            var transportType = Microsoft.Azure.EventHubs.TransportType.Amqp;
            if (!string.IsNullOrEmpty(ehUpstreamProtocol))
            {
                if (ehUpstreamProtocol.ToUpper() == "AMQP")
                {
                    // do nothing. Amqp is default anyway
                }
                else if (ehUpstreamProtocol.ToUpper() == "AMQPWS")
                {
                    transportType = Microsoft.Azure.EventHubs.TransportType.AmqpWebSockets;
                }
                else
                {
                    throw new Exception($"Unsupported parameter for EventHubUpstreamProtocol={ehUpstreamProtocol}. Only supported values are Amqp or AmqpWs");
                }
            }

            var connectionStringBuilder = new EventHubsConnectionStringBuilder(eventHubConnectionString)
            {
                TransportType = transportType
            };

            logger.LogInformation($"Initializing EventHubClient to Endpoint={connectionStringBuilder.Endpoint} EntityPath={connectionStringBuilder.EntityPath} with TransportType={transportType} ...");
            var eventHubClient = EventHubClient.CreateFromConnectionString(connectionStringBuilder.ToString());
            logger.LogInformation($"EventHubClient initialized using {transportType.ToString()}");

            return eventHubClient;
        }
    }
}