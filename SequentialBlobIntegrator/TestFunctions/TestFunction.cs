using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Azure.Storage.Blobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System.Collections.Generic;
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
            List<Task> li = new();

            for (int i = 0; i < 1; i++)
            {
                // create test json data
                JObject jobject = new()
                {
                    { "Firstname", "Pudding" },
                    { "Lastname", "McTwinkle" }
                };

                // create test row keys
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

                await Task.Delay(1);

                // create a test integration payload
                IntegrationPayload ipayload = new()
                {                   
                    IntegrationHttpRequest = new IntegrationHttpRequest()
                    {
                        HttpMethod = "post",
                        Content = jobject.ToString(Formatting.None),
                        Url = "https://httpbin.org/post"
                    },
                    Key = "23423423" + i,
                    TicksStamp = DateTime.Now.Ticks
                };

                for (int j = 0; j < 4; j++)
                {
                    // call the create bllob endpoint 
                    li.Add(httpClient.PostAsync("http://localhost:7161/CreateBlob", new StringContent(JsonConvert.SerializeObject(ipayload))));

                    await Task.Delay(1000);

                    ipayload.TicksStamp += 1;
                }
            }

            await Task.WhenAll(li);

            return new HttpResponseMessage();
        }
    }
}
