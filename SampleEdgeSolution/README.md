# Azure IoT Edge Sample solution

This solution contains various sample custom modules (custom code as well as Azure Function). The modules implement various patters which I would consider good pratice for Edge modules, for instance in terms of logging.
You can find examples for Direct Method invocation, twin (desired properties) updates, and communication of modules outside of Edge Hub.

Builds of the modules for Linux, ARM und Windows are pushed to my docker repo, feel free to pull them from there: https://hub.docker.com/u/sebader

## Current list of modules
* DataGenerator (C# custom module)
  - Sample data generator that sends data into Edge Hub
  - Implements callback for desired properties updates
* HttpRestClient (C# custom module)
  - Module that only reacts to Direct Method invocations. Does not send or receive messages to/from Edge Hub
* EventHubForwader (Azure Function C#)
  - Function that receives input from the Edge Hub and sends the data to an external Azure Event Hub

## Notes
Where applicable, the transport protocol, i.e. via which protocol a module connects to the Edge Hub, can be controlled via the environment variable "TransportProtocol". This accepts either AMQP or MQTT. Currently my modules default to AMQP but both should be feature-equivalent.
