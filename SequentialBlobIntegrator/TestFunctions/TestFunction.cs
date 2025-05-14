using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Azure.Storage.Blobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using SequentialBlobIntegrator.Models;

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

            for (int i = 0; i < 2; i++)
            {
                TestJsonContent content = new()
                {
                    Firstname = "Pudding",
                    Lastname = "McTwinkle"
                };
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

                RootPayload rootpayload = new()
                {
                    Url = "https://httpbin.org/get",
                    Key = "23423423" + i,
                    Content = JsonConvert.SerializeObject(content),
                    TicksStamp = DateTime.Now.Ticks,
                    HttpMethod = "get"
                };

                li.Add(httpClient.PostAsync("http://localhost:7161/CreateBlob", new StringContent(JsonConvert.SerializeObject(rootpayload))));

                await Task.Delay(1000);

                rootpayload.TicksStamp += 1;

                li.Add(httpClient.PostAsync("http://localhost:7161/CreateBlob", new StringContent(JsonConvert.SerializeObject(rootpayload))));
                await Task.Delay(1000);

                rootpayload.TicksStamp += 1;

                li.Add(httpClient.PostAsync("http://localhost:7161/CreateBlob", new StringContent(JsonConvert.SerializeObject(rootpayload))));
                await Task.Delay(1000);

                rootpayload.TicksStamp += 1;

                li.Add(httpClient.PostAsync("http://localhost:7161/CreateBlob", new StringContent(JsonConvert.SerializeObject(rootpayload))));
                await Task.Delay(1000);

                rootpayload.TicksStamp += 1;

                li.Add(httpClient.PostAsync("http://localhost:7161/CreateBlob", new StringContent(JsonConvert.SerializeObject(rootpayload))));
                await Task.Delay(1000);

                rootpayload.TicksStamp += 1;

                li.Add(httpClient.PostAsync("http://localhost:7161/CreateBlob", new StringContent(JsonConvert.SerializeObject(rootpayload))));
                await Task.Delay(1000);
            }

            await Task.WhenAll(li);

            return new HttpResponseMessage();
        }

        [FunctionName(nameof(CreateBlob))]
        public async Task<HttpResponseMessage> CreateBlob(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            RootPayload input = await req.Content.ReadFromJsonAsync<RootPayload>();

            string ticks = (input.TicksStamp + 1000000000000000000).ToString();

            string blobname = $"{input.Key}/{ticks}";

            await blobContainerClient.UploadBlobAsync(blobname, BinaryData.FromObjectAsJson(input));

            string content = string.Empty;

            return new HttpResponseMessage()
            {
                Content = new StringContent(content)
            };
        }
    }
}
