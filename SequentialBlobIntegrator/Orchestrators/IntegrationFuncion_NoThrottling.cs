using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using SequentialBlobIntegrator.Models;

namespace SequentialBlobIntegrator
{
    public class IntegrationFuncion_NoThrottling
    {
        [Deterministic]
        [FunctionName(nameof(MainOrchestrator))]
        public static async Task MainOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context, ILogger log)
        {
            string[] arr = context.InstanceId.Split('|');
            string key = arr[0];
            long ticks = Convert.ToInt64(arr[1]);

            ILogger logger = context.CreateReplaySafeLogger(log);
            logger.LogError(context.CurrentUtcDateTime.ToString());

            EntityId id = new(nameof(EntityFunctions.LockByKey), key + "|nothrottle");

            bool haslock = false;

            try
            {
                while (true)
                {
                    using (await context.LockAsync(id))
                    {
                        Lock loc = await context.CallEntityAsync<Lock>(id, "getlock");

                        if (loc.TicksStamp > ticks)
                        {
                            return;
                        }

                        if (!loc.IsLocked)
                        {
                            loc.IsLocked = true;

                            await context.CallEntityAsync(id, "lock", loc);

                            haslock = true;

                            break;
                        }
                    }

                    DateTime deadline = context.CurrentUtcDateTime.Add(TimeSpan.FromSeconds(5));
                    await context.CreateTimer(deadline, CancellationToken.None);
                }

                string token = $"{key}/0";

                while (true)
                {
                    (token, List<string> blobs, ticks) = await context.CallActivityAsync<(string, List<string>, long)>(nameof(BlobFunctions.GetBlobs), token);

                    if (blobs.Count == 0)
                    {
                        if (string.IsNullOrEmpty(token))
                        {
                            break;
                        }

                        continue;
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

                    if (string.IsNullOrEmpty(token))
                    {
                        break;
                    }
                }

                logger.LogWarning("!!!!!!!!!!!!!!!<<<<<<< DONE >>>>>>>!!!!!!!!!!!!!!!   KEY: " + key);
            }
            catch (Exception ex)
            {
                throw;
            }
            finally
            {
                if (haslock)
                {
                    await context.CallEntityAsync(id, "lock", new Lock() { IsLocked = false, TicksStamp = ticks });
                }
            }
        }
    }
}