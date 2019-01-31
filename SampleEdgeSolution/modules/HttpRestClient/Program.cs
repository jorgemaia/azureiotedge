namespace HttpRestClient
{
    using System;
    using System.Net.Http;
    using System.Runtime.Loader;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Client;
    using Newtonsoft.Json;
    using Serilog;
    using helpers;

    /// <summary>
    /// Sample Azure IoT Edge module that implements sample Direct Method callbacks.
    /// This module can execute HTTP REST call against arbirary endpoints, e.g. when running inside a firewalled network on-prem
    /// The result of the REST call is being returned to the caller of the direct method
    /// For now this supports GET and POST requests
    /// </summary>
    class Program
    {
        private const int DefaultTimeoutSeconds = 30;
        private static HttpClient _httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(DefaultTimeoutSeconds) };

        public static int Main() => MainAsync().Result;

        static async Task<int> MainAsync()
        {
            EdgeModuleHelpers.InitLogging();
            Log.Information($"Module {Environment.GetEnvironmentVariable("IOTEDGE_MODULEID")} starting up...");
            var moduleClient = await EdgeModuleHelpers.Init();

            // Register direct method handlers
            await moduleClient.SetMethodHandlerAsync("ExecuteGet", ExecuteGet, moduleClient);
            await moduleClient.SetMethodHandlerAsync("ExecutePost", ExecutePost, moduleClient);

            await moduleClient.SetMethodDefaultHandlerAsync(DefaultMethodHandler, moduleClient);

            // Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
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
        /// Default method handler for any method calls which are not implemented
        /// </summary>
        /// <param name="methodRequest"></param>
        /// <param name="userContext"></param>
        /// <returns></returns>
        private static Task<MethodResponse> DefaultMethodHandler(MethodRequest methodRequest, object userContext)
        {
            Log.Information($"Received method invocation for non-existing method {methodRequest.Name}. Returning 404.");
            var result = new RestMethodResponsePayload() { Error = $"Method {methodRequest.Name} not implemented" };
            var outResult = JsonConvert.SerializeObject(result, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            return Task.FromResult(new MethodResponse(Encoding.UTF8.GetBytes(outResult), 404));
        }

        /// <summary>
        /// Handler for GET requests
        /// </summary>
        /// <param name="methodRequest"></param>
        /// <param name="userContext"></param>
        /// <returns></returns>
        private static async Task<MethodResponse> ExecuteGet(MethodRequest methodRequest, object userContext)
        {
            var request = JsonConvert.DeserializeObject<RestMethodRequestPayload>(methodRequest.DataAsJson);
            return await ExecuteRestCall(request, HttpMethod.Get);
        }

        /// <summary>
        /// Handler for POST requests
        /// </summary>
        /// <param name="methodRequest"></param>
        /// <param name="userContext"></param>
        /// <returns></returns>
        private static async Task<MethodResponse> ExecutePost(MethodRequest methodRequest, object userContext)
        {
            var request = JsonConvert.DeserializeObject<RestMethodRequestPayload>(methodRequest.DataAsJson);
            if (string.IsNullOrEmpty(request.RequestPayload))
            {
                throw new ArgumentException($"No request payload for POST request supplied.");
            }
            return await ExecuteRestCall(request, HttpMethod.Post);
        }

        /// <summary>
        /// Function to call external REST endpoints
        /// </summary>
        /// <param name="request"></param>
        /// <param name="httpMethod"></param>
        /// <returns></returns>
        private static async Task<MethodResponse> ExecuteRestCall(RestMethodRequestPayload request, HttpMethod httpMethod)
        {
            Log.Information($"Received REST call request for method {httpMethod.ToString()}");
            var result = new RestMethodResponsePayload();
            int returnCode = 200;

            try
            {
                if (request == null || string.IsNullOrEmpty(request.Url))
                {
                    result.Error = "No valid method payload received. At least Url parameter required in request payload.";
                    returnCode = 400;

                    Log.Error("No valid method payload received");
                }
                else
                {
                    result.ClientUrl = request.Url;
                    Log.Information($"Executing HTTP {httpMethod} method against endpoint {request.Url} with timeout of {_httpClient.Timeout}");

                    HttpResponseMessage response;
                    if (httpMethod == HttpMethod.Get)
                    {
                        response = await _httpClient.GetAsync(request.Url).ConfigureAwait(false);
                    }
                    else if (httpMethod == HttpMethod.Post)
                    {
                        // For now we exepct - and only support - JSON payload
                        var content = new StringContent(request.RequestPayload, Encoding.UTF8, "application/json");
                        response = await _httpClient.PostAsync(request.Url, content).ConfigureAwait(false);
                    }
                    else
                    {
                        throw new NotImplementedException($"HTTP method {httpMethod.ToString()} is currently not supported by the module.");
                    }

                    response.EnsureSuccessStatusCode();
                    Log.Information($"HTTP operation successfully completed StatusCode={response.StatusCode}.");

                    result.ClientResponse = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    Log.Debug($"Endpoint response:");
                    Log.Debug(result.ClientResponse);
                }
            }
            catch (HttpRequestException hre)
            {
                Log.Error($"Response code: {hre.ToString()}");
                result.Error = hre.Message;
                returnCode = 500;
            }
            catch (Exception ex)
            {
                Log.Error($"Exception: {ex.Message}");
                result.Error = ex.Message;
                returnCode = 500;
            }
            var outResult = JsonConvert.SerializeObject(result, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            return new MethodResponse(Encoding.UTF8.GetBytes(outResult), returnCode);
        }
    }

    /// <summary>
    /// Payload of direct method request
    /// </summary>
    class RestMethodRequestPayload
    {
        public string Url { get; set; }
        public string RequestPayload { get; set; }
    }

    /// <summary>
    /// Payload of direct method response
    /// </summary>
    class RestMethodResponsePayload
    {
        public string ClientUrl { get; set; } = null;
        public string Error { get; set; } = null;
        public string ClientResponse { get; set; } = null;
    }
}
