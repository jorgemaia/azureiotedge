# azureiotedge
This is a custom C# based module specifically to measure performance (throughput) of an Azure IoT Edge system.
Currently has two inputs configured:
1) Raw input: You can route any messages into this one and it will measure throughput based on how many messages flow into the module in what amount of time.
2) ASA input: This one takes messages from an Azure Stream Analytics module on the Edge (was somewhat specific to one use case I am testing)

The module can make use of Azure Application Insights to write metrics to there. For this you need to set your App Insights API key as an environment variable ("ApplicationInsightsApiKey") when creating the module. Otherwise it will just log console.
