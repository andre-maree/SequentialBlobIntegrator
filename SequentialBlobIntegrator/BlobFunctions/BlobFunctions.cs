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
using System.IO;
using Newtonsoft.Json;
using System.Text;
using SequentialBlobIntegrator.Models;
using Microsoft.Azure.WebJobs.Extensions.Http;
using System.Net.Http.Json;

namespace SequentialBlobIntegrator
{
    public class BlobFunctions
    {
        private BlobContainerClient blobContainerClient;
        private readonly HttpClient httpClient;

        public BlobFunctions(BlobServiceClient _blobServiceClient, IHttpClientFactory httpClientFactory)
        {
            blobContainerClient = _blobServiceClient.GetBlobContainerClient(Environment.GetEnvironmentVariable("Container"));

            httpClient = httpClientFactory.CreateClient();
        }

        [FunctionName(nameof(CreateBlob))]
        public async Task<HttpResponseMessage> CreateBlob(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            IntegrationPayload input = await req.Content.ReadFromJsonAsync<IntegrationPayload>();

            string ticks = (input.TicksStamp + 1000000000000000000).ToString();

            string blobname = $"{input.Key}/{ticks}";

            await blobContainerClient.UploadBlobAsync(blobname, BinaryData.FromObjectAsJson(input.IntegrationHttpRequest));

            string content = string.Empty;

            return new HttpResponseMessage()
            {
                Content = new StringContent(content)
            };
        }

        [FunctionName("BlobTriggerCSharp")]
        public static async Task Run([BlobTrigger("%Container%/{name}")] Stream myBlob, string name, [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            string[] arr = name.Split('/');

            // with throttling
            await starter.StartNewAsync(nameof(IntegrationFuncion_WithThrottling.MainThrottledOrchestrator), $"{arr[0]}|{arr[1]}");
            
            // with no throttling
            //await starter.StartNewAsync(nameof(IntegrationFuncion_NoThrottling.MainOrchestrator), $"{arr[0]}|{arr[1]}");
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

                var res = await resp.Content.ReadAsStringAsync();

                log.LogCritical("Calling external endpoint: " + resp.StatusCode + " : " + await resp.Content.ReadAsStringAsync());// + res.Substring(10));

                await Task.Delay(1000);
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

            IAsyncEnumerable<Page<BlobItem>> blobResultSegment = blobContainerClient.GetBlobsAsync(prefix: key + '/').AsPages(token, 5);

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

        [FunctionName(nameof(GetBlobs_ThrottledByKey))]
        public async Task<(bool, List<string>)> GetBlobs_ThrottledByKey([ActivityTrigger] (string key, int pageSize) input, ILogger log)
        {
            List<string> result = [];

            bool goon = true;
            string token = null;

            do
            {
                IAsyncEnumerable<Page<BlobItem>> blobResultSegment = blobContainerClient.GetBlobsAsync(prefix: input.key).AsPages(token, 5);
                await foreach (Page<BlobItem> blobPage in blobResultSegment)
                {
                    foreach (BlobItem blob in blobPage.Values)
                    {
                        result.Add(blob.Name);
                    }

                    if (result.Count >= 5)
                    {
                        goon = blobPage.ContinuationToken != null;

                        token = string.Empty;

                        break;
                    }

                    if (string.IsNullOrEmpty(blobPage.ContinuationToken))
                    {
                        goon = false;

                        token = string.Empty;

                        break;
                    }

                    token = blobPage.ContinuationToken;
                }
            }
            while (!string.IsNullOrEmpty(token));

            log.LogCritical("Get blobs: " + result.Count);


            return (goon, result);
        }

        //[FunctionName(nameof(CheckStatus))]
        //public async Task<(bool, string)> CheckStatus([ActivityTrigger] (string key, long ticks) input, [DurableClient] IDurableOrchestrationClient starter, ILogger log)
        //{
        //    DurableOrchestrationStatus result = await starter.GetStatusAsync(input.key);

        //    if (result == null)
        //    {
        //        return (true, input.key + "/0");
        //    }

        //    //if(result.RuntimeStatus.ToString().Equals("Running"))
        //    //{
        //    //    Console.WriteLine("BBBBBBBBBBBBBBBBBLLLLLLLLLLLLLLLLLLLLLLLOOOOOOOOOOOOOOOCCCCCCCCCCCCCCCCCCCCCCKKKKKKKKKKKKKKKKKKKEEEEEEEEEEEEEEEEEEEEEDDDDDDDDDDDDDDDDD");

        //    //    return false;
        //    //}


        //    //if (result.Input.Type == JTokenType.Null)
        //    //{
        //    //    Console.WriteLine("BBBBBBBBBBBBBBBBBLLLLLLLLLLLLLLLLLLLLLLLOOOOOOOOOOOOOOOCCCCCCCCCCCCCCCCCCCCCCKKKKKKKKKKKKKKKKKKKEEEEEEEEEEEEEEEEEEEEEDDDDDDDDDDDDDDDDD");

        //    //    return false;
        //    //}
        //    string token = result.Input.ToString();

        //    if (Convert.ToInt64(token.Split('/')[1]) >= input.ticks)
        //    {
        //        //Console.WriteLine("BBBBBBBBBBBBBBBBBLLLLLLLLLLLLLLLLLLLLLLLOOOOOOOOOOOOOOOCCCCCCCCCCCCCCCCCCCCCCKKKKKKKKKKKKKKKKKKKEEEEEEEEEEEEEEEEEEEEEDDDDDDDDDDDDDDDDD");

        //        return (false, string.Empty);
        //    }

        //    Console.WriteLine("CCCCCCCAAAAAAAANNNNNNNNNNNNNN RRRRRRRUUUUUUUUUUUUUUUNNNNNNNNNNNNNNNN");

        //    return (true, token);
        //}
    }
}