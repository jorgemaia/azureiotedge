# Azure IoT Edge Sample solution

This solution contains various sample custom modules (custom code as well as Azure Function). The modules implement various patters which I would consider good pratice for Edge modules, for instance in terms of logging.
You can find examples for Direct Method invocation, twin (desired properties) updates, and communication of modules outside of Edge Hub.

Builds of the modules are pushed to my docker repo, feel free to pull them from there: https://hub.docker.com/u/sebader

## Current list of modules
* DataGenerator (C# custom module)
  - Sample data generator that sends data into Edge Hub
  - Implements callback for desired properties updates
* HttpRestClient (C# custom module)
  - Module to react to Direct Method invocations
* EventHubForwader (Azure Function C#)
  - Function that takes input from the Edge Hub and sends data to an external Azure Event Hub
