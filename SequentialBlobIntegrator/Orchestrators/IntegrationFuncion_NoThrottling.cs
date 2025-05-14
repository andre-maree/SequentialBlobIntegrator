using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace SequentialBlobIntegrator
{
    public class IntegrationFuncion_NoThrottling
    {
        //private BlobContainerClient blobContainerClient;
        //private HttpClient httpClient;

        //public IntegrationFuncion_NoThrottling(BlobServiceClient _blobServiceClient, IHttpClientFactory httpClientFactory)
        //{
        //    blobContainerClient = _blobServiceClient.GetBlobContainerClient(Environment.GetEnvironmentVariable("Container"));

        //    httpClient = httpClientFactory.CreateClient();
        //}

        [Deterministic]
        [FunctionName(nameof(MainOrchestrator))]
        public static async Task MainOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context, ILogger log)
        {
            string[] arr = context.InstanceId.Split('|');
            string key = arr[0];

            ILogger logger = context.CreateReplaySafeLogger(log);
            logger.LogError(context.CurrentUtcDateTime.ToString());

            EntityId id = new(nameof(EntityFunctions.LockByKey), key);

            try
            {
                while (true)
                {
                    using (await context.LockAsync(id))
                    {
                        bool islocked = await context.CallEntityAsync<bool>(id, "getlock");

                        if (!islocked)
                        {
                            await context.CallEntityAsync(id, "lock");

                            break;
                        }
                    }

                    DateTime deadline = context.CurrentUtcDateTime.Add(TimeSpan.FromSeconds(5));
                    await context.CreateTimer(deadline, CancellationToken.None);
                }

                List<string> blobs = new(); 

                while (true)
                {
                    (bool goon, List<string> outblobs) = await context.CallActivityAsync<(bool, List<string>)>(nameof(BlobFunctions.GetBlobs_ThrottledByKey), (key, 5));

                    if(!goon)
                    {
                        if(outblobs.Count > 0)
                        {
                            blobs.AddRange(outblobs);
                            break;
                        }

                        return;
                    }

                    blobs.AddRange(outblobs);

                    if (blobs.Count >= 5)
                    {
                        break;
                    }
                }

                Task blobtask = null;
                bool waitforblob = false;

                foreach (string blob in blobs)
                {
                    if (waitforblob)
                    {
                        await blobtask;
                    }

                    await context.CallActivityAsync(nameof(BlobFunctions.CallExternalHttp), blob);

                    waitforblob = true;

                    blobtask = context.CallActivityAsync(nameof(BlobFunctions.DeleteBlob), blob);
                }

                if (waitforblob)
                {
                    await blobtask;
                }

                logger.LogWarning(context.CurrentUtcDateTime.ToString());
            }
            catch (Exception ex)
            {

                int r = 0;
            }
            finally
            {
                await context.CallEntityAsync(id, "unlock");
            }
        }

        [FunctionName("Function1_HttpStart")]
        public static async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {

            string key = "23423423";
            // Function input comes from the request content.
            string instanceId = await starter.StartNewAsync("Function1", input: key);

            log.LogInformation("Started orchestration with ID = '{instanceId}'.", instanceId);

            return starter.CreateCheckStatusResponse(req, instanceId);
        }
    }
}