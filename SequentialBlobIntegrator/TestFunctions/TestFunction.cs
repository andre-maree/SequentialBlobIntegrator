using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Azure.Storage.Blobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System.Net.Http;
using SequentialBlobIntegrator.Models;
using Newtonsoft.Json.Linq;

namespace SequentialBlobIntegrator.TestFunctions
{
    public class TestFunction
    {
        private BlobContainerClient blobContainerClient;
        private readonly HttpClient httpClient;

        public TestFunction(BlobServiceClient _blobServiceClient, IHttpClientFactory httpClientFactory)
        {
            blobContainerClient = _blobServiceClient.GetBlobContainerClient(Environment.GetEnvironmentVariable("Container"));

            httpClient = httpClientFactory.CreateClient();
        }

        [FunctionName("testFunction1_HttpStart")]
        public async Task<HttpResponseMessage> testFunction1_HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            await blobContainerClient.CreateIfNotExistsAsync();

            bool togle = false;

            long ticks = DateTime.Now.Ticks;

            // loop and create test integration instances
            for (int i = 0; i < 10; i++)
            {
                // create test json data
                JObject jobject = new()
                {
                    { "Firstname", "Pudding " + i },
                    { "Lastname", "McTwinkle" }
                };

                // create test row keys, use this toggle for creating 2 keys,
                // or create a key for each i counter
                string key = string.Empty;
                if (togle)
                {
                    key += "0";
                }
                else
                {
                    key += "1";
                }
                togle = !togle;

                // create a test integration payload
                IntegrationPayload ipayload = new()
                {
                    IntegrationHttpRequest = new IntegrationHttpRequest()
                    {
                        HttpMethod = "post",
                        Content = jobject.ToString(Formatting.None),
                        HttpRoute = "/post"
                        //HttpRoute = "/TestHttpCall"
                    },
                    Key = "23423423" + i,//key // key or i
                };

                // create some instances for each key
                for (int j = 0; j < 5; j++)
                {
                    //ipayload.Key = "23423423";
                    ipayload.TicksStamp = DateTime.UtcNow.Ticks;

                    // call the create blob endpoint 
                    await httpClient.PostAsync("http://localhost:7161/CreateIntegrationInstance", new StringContent(JsonConvert.SerializeObject(ipayload)));

                    await Task.Delay(1000);
                }
            }

            return new HttpResponseMessage();
        }

        [FunctionName("TestHttpCall")]
        public async Task<HttpResponseMessage> TestHttpCall(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            await Task.Delay(1000);

            return new HttpResponseMessage()
            {
                Content = new StringContent( await req.Content.ReadAsStringAsync())
            };
        }
    }
}