#r "Microsoft.Azure.Devices.Client"
#r "Newtonsoft.Json"
#r "System.Data.SqlClient"

using System.IO;
using Microsoft.Azure.Devices.Client;
using Newtonsoft.Json;
using Sql = System.Data.SqlClient;
using System.Threading.Tasks;

// Filter messages based on the temperature value in the body of the message and the temperature threshold value.
public static async Task Run(Message messageReceived, TraceWriter log)
{
    byte[] messageBytes = messageReceived.GetBytes();
    var messageString = System.Text.Encoding.UTF8.GetString(messageBytes);

    if (!string.IsNullOrEmpty(messageString))
    {
        // Get the body of the message and deserialize it
        var messageBody = JsonConvert.DeserializeObject<MessageBody>(messageString);
    
        string sqlConnString = Environment.GetEnvironmentVariable("SQLConnectionString");

        //Store the data in SQL db
        using (Sql.SqlConnection conn = new Sql.SqlConnection(sqlConnString))
        {
            conn.Open();
            var insertRowStatement = $"INSERT INTO MeasurementsDB.dbo.TemperatureHumidity VALUES (CONVERT(DATETIME2,'{messageBody.deviceTime}', 127), '{messageBody.deviceId}', {messageBody.temperatur}, {messageBody.humidity});"; 
            using (Sql.SqlCommand cmd = new Sql.SqlCommand(insertRowStatement, conn))
            {
                //Execute the command and log the # rows affected.
                var rows = await cmd.ExecuteNonQueryAsync();
                log.Info($"{rows} rows were inserted");
            }
        }
/*
        if (messageBody != null)
        {
            // Send the message to the output as the temperature value is greater than the threshold
            var filteredMessage = new Message(messageBytes);
            // Copy the properties of the original message into the new Message object
            foreach (KeyValuePair<string, string> prop in messageReceived.Properties)
            {
                filteredMessage.Properties.Add(prop.Key, prop.Value);
            }
            // Add a new property to the message to indicate it is an alert
            filteredMessage.Properties.Add("MessageType", "Alert");
            // Send the message        
            await output.AddAsync(filteredMessage);
            log.Info("Received and transferred a message with temperature above the threshold");
        }
*/
    }
}

//Define the expected schema for the body of incoming messages
class MessageBody
{
    public string deviceId {get;set;}
    public string messageId {get;set;}
    public string temperatur {get;set;}
    public string humidity {get;set;}
    public string deviceTime {get; set;}
}