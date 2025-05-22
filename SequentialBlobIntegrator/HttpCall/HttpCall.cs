using Azure.Storage.Blobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using Azure.Storage.Blobs.Models;
using Azure;
using Newtonsoft.Json;
using System.Text;
using SequentialBlobIntegrator.Models;

namespace SequentialBlobIntegrator
{
    public class HttpCall
    {
        private BlobContainerClient blobContainerClient;
        private readonly HttpClient httpClient;

        public HttpCall(BlobServiceClient _blobServiceClient, IHttpClientFactory httpClientFactory)
        {
            blobContainerClient = _blobServiceClient.GetBlobContainerClient(Environment.GetEnvironmentVariable("Container"));

            httpClient = httpClientFactory.CreateClient("HttpBinHttpClient");
        }

        [FunctionName(nameof(CallExternalHttp))]
        public async Task CallExternalHttp([ActivityTrigger] string blobname, ILogger log)
        {
            try
            {
                BlobClient blobClient = blobContainerClient.GetBlobClient(blobname);

                Response<BlobDownloadResult> result = await blobClient.DownloadContentAsync();

                IntegrationHttpRequest irequest = JsonConvert.DeserializeObject<IntegrationHttpRequest>(result.Value.Content.ToString());

                HttpResponseMessage resp = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Parse(irequest.HttpMethod), irequest.HttpRoute)
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
    }
}