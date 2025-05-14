using SequentialBlobIntegrator.Models;

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

        [FunctionName("BlobTriggerCSharp")]
        public static async Task Run([BlobTrigger("%Container%/{name}")] Stream myBlob, string name, [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            string[] arr = name.Split('/');

            await starter.StartNewAsync(nameof(IntegrationFuncion_WithThrottling.MainThrottledOrchestrator), $"{arr[0]}|{arr[1]}");
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
                var r = 0;
            }
        }

        [FunctionName(nameof(CallExternalHttp))]
        public async Task CallExternalHttp([ActivityTrigger] string blobname, ILogger log)
        {
            try
            {
                BlobClient blobClient = blobContainerClient.GetBlobClient(blobname);

                Response<BlobDownloadResult> result = await blobClient.DownloadContentAsync();

                RootPayload blobpayload = JsonConvert.DeserializeObject<RootPayload>(result.Value.Content.ToString());
                var rr = result.Value.Content.ToString();

                HttpResponseMessage resp = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Parse(blobpayload.HttpMethod), blobpayload.Url)
                {
                    Content = new StringContent(blobpayload.Content, Encoding.UTF8, "application/json")
                    
                });
                var res = await resp.Content.ReadAsStringAsync();
                Console.WriteLine("Calling external endpoint: " + resp.StatusCode);// + res.Substring(10));

                await Task.Delay(1000);
            }

            catch (Exception ex)
            {
                var r = 0;

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

            log.LogCritical("Get blobs: " + result.Count);

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