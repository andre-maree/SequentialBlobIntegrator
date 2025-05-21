using Azure.Storage.Blobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using Azure.Storage.Blobs.Models;
using Azure;
using Newtonsoft.Json;
using System.Text;
using SequentialBlobIntegrator.Models;
using Microsoft.Azure.WebJobs.Extensions.Http;
using System.Net.Http.Json;
using Azure.Storage.Queues.Models;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using Azure.Storage.Queues;

namespace SequentialBlobIntegrator
{
    public class BlobFunctions
    {
        private BlobContainerClient blobContainerClient;
        private readonly HttpClient httpClient;
        private readonly BlobClient blobClient;

        public BlobFunctions(BlobServiceClient _blobServiceClient, IHttpClientFactory httpClientFactory)
        {
            blobContainerClient = _blobServiceClient.GetBlobContainerClient(Environment.GetEnvironmentVariable("Container"));

            httpClient = httpClientFactory.CreateClient();
        }

        [FunctionName(nameof(CreateIntegrationInstance))]
        public async Task<HttpResponseMessage> CreateIntegrationInstance(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            IntegrationPayload input = await req.Content.ReadFromJsonAsync<IntegrationPayload>();

            if (string.IsNullOrWhiteSpace(input.Key))
            {
                throw new Exception("Invalid integration key.");
            }
            else if (input.TicksStamp <= 0)
            {
                throw new Exception("Invalid ticks stamp.");
            }

            string ticks = (input.TicksStamp + 1000000000000000000).ToString();

            string blobname = $"{input.Key}/{ticks}";

            BlobClient blobClient = blobContainerClient.GetBlobClient(blobname);

            await blobClient.UploadAsync(BinaryData.FromObjectAsJson(input.IntegrationHttpRequest), overwrite: true);

            string content = string.Empty;

            return new HttpResponseMessage()
            {
                Content = new StringContent(content)
            };
        }

        [FunctionName("BlobTriggerCSharp")]
        public async Task Run([BlobTrigger("%Container%/{name}")] string data, string name, [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            string[] arr = name.Split('/');

            IntegrationHttpRequest irequest = null;

            try
            {
                irequest = JsonConvert.DeserializeObject<IntegrationHttpRequest>(data);
            }
            catch (Exception ex)
            {
                dynamic jsonObject = new JObject();
                jsonObject.Error = "IntegrationHttpRequest json parsing error";
                jsonObject.BlobName = name;
                jsonObject.IntegrationHttpRequest = data;

                await blobContainerClient.DeleteBlobIfExistsAsync(name);

                return;
            }

            bool validUrl = Uri.TryCreate(irequest.Url, UriKind.Absolute, out Uri uriResult)
                && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);

            if (!validUrl)
            {
                dynamic jsonObject = new JObject();
                jsonObject.Error = "invalid url error";
                jsonObject.BlobName = name;
                jsonObject.IntegrationHttpRequest = data;

                await blobContainerClient.DeleteBlobIfExistsAsync(name);

                return;
            }

            HttpMethod httpverb = HttpMethod.Parse(irequest.HttpMethod);

            if (httpverb != HttpMethod.Post && httpverb != HttpMethod.Put && httpverb != HttpMethod.Get && httpverb != HttpMethod.Delete && httpverb != HttpMethod.Patch && httpverb != HttpMethod.Options && httpverb != HttpMethod.Trace && httpverb != HttpMethod.Connect && httpverb != HttpMethod.Head)
            {
                dynamic jsonObject = new JObject();
                jsonObject.Error = "invalid http method error";
                jsonObject.BlobName = name;
                jsonObject.IntegrationHttpRequest = data;

                await blobContainerClient.DeleteBlobIfExistsAsync(name);

                return;
            }

            try
            {
                // with throttling
                await starter.StartNewAsync(nameof(IntegrationFuncion_WithThrottling.MainThrottledOrchestrator), $"{arr[0]}|{arr[1]}");

                // with no throttling
                //await starter.StartNewAsync(nameof(IntegrationFuncion_NoThrottling.MainOrchestrator), $"{arr[0]}|{arr[1]}");
            }
            catch (Exception ex)
            {
                int retrycount = 1;

                while (true)
                {
                    await Task.Delay(retrycount * 1000);

                    try
                    {
                        await starter.StartNewAsync(nameof(IntegrationFuncion_WithThrottling.MainThrottledOrchestrator), $"{arr[0]}|{arr[1]}");

                        break;
                    }
                    catch (Exception ex2)
                    {
                        if (retrycount == 5)
                        {
                            // log instance start failure
                            throw;
                        }

                        retrycount++;
                    }
                }
            }
        }

        [FunctionName("BlobPoisonQueueTrigger")]
        public async Task BlobPoisonQueueTrigger(
    [QueueTrigger("webjobs-blobtrigger-poison")] QueueMessage myQueueItem, [DurableClient] IDurableOrchestrationClient starter, ILogger log)
        {
            try
            {
                JObject dyn = JObject.Parse(myQueueItem.Body.ToString());

                string[] arr = dyn.GetValue("BlobName").ToString().Split('/');

                await starter.StartNewAsync(nameof(IntegrationFuncion_WithThrottling.MainThrottledOrchestrator), $"{arr[0]}|{arr[1]}");
            }
            catch (Exception ex)
            {
                QueueClient qclient = new(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "webjobs-blobtrigger-poison");

                long calc = myQueueItem.DequeueCount * 2;
                calc = calc > 10 ? 10 : calc;

                await qclient.UpdateMessageAsync(myQueueItem.MessageId,myQueueItem.PopReceipt, visibilityTimeout: TimeSpan.FromMinutes(calc));
            }
        }

        [FunctionName(nameof(DeleteBlob))]
        public async Task DeleteBlob([ActivityTrigger] string blob, ILogger log)
        {
            try
            {
                await blobContainerClient.DeleteBlobIfExistsAsync(blob);

                log.LogError("Deleted blob: " + blob);
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        [FunctionName(nameof(CallExternalHttp))]
        public async Task CallExternalHttp([ActivityTrigger] string blobname, ILogger log)
        {
            try
            {
                BlobClient blobClient = blobContainerClient.GetBlobClient(blobname);

                Response<BlobDownloadResult> result = await blobClient.DownloadContentAsync();

                IntegrationHttpRequest irequest = JsonConvert.DeserializeObject<IntegrationHttpRequest>(result.Value.Content.ToString());

                HttpResponseMessage resp = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Parse(irequest.HttpMethod), irequest.Url)
                {
                    Content = new StringContent(irequest.Content, Encoding.UTF8, "application/json")
                });

                if (!resp.IsSuccessStatusCode)
                {
                    throw new HttpRequestException("The call failed with a response code: " + resp.StatusCode);
                }

                log.LogCritical("Called the external endpoint: " + resp.StatusCode + " : " + await resp.Content.ReadAsStringAsync());// + res.Substring(10));

                //await Task.Delay(1000);
            }

            catch (Exception ex)
            {
                throw;
            }
        }

        [FunctionName(nameof(GetBlobs))]
        public async Task<(string, List<string>, long lasticks)> GetBlobs([ActivityTrigger] string token, ILogger log)
        {
            List<string> result = [];

            string[] arr = token.Split('/');
            long lastticks = Convert.ToInt64(arr[1]);
            string key = arr[0];
            int blobBatchSize = Convert.ToInt32(Environment.GetEnvironmentVariable("BlobBatchSize"));

            IAsyncEnumerable<Page<BlobItem>> blobResultSegment = blobContainerClient.GetBlobsAsync(prefix: key + '/').AsPages(token, blobBatchSize);

            await foreach (Page<BlobItem> blobPage in blobResultSegment)
            {
                foreach (BlobItem blob in blobPage.Values)
                {
                    result.Add(blob.Name);
                }

                token = blobPage.ContinuationToken;

                break;
            }

            string last = result.LastOrDefault();

            if (last != null)
            {
                lastticks = Convert.ToInt64(last.Split('/')[1]);
            }

            log.LogCritical("Get blobs: " + result.Count + " for key: " + key);

            return (token, result, lastticks);
        }
    }
}