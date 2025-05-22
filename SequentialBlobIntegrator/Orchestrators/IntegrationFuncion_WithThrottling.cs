using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using SequentialBlobIntegrator.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SequentialBlobIntegrator
{
    public class IntegrationFuncion_WithThrottling
    {
        private static readonly RetryOptions retryOptions = new(TimeSpan.FromSeconds(Convert.ToInt32(Environment.GetEnvironmentVariable("RetryFirstIntervalSeconds"))), Convert.ToInt32(Environment.GetEnvironmentVariable("RetryMaxIntervals")))
        {
            BackoffCoefficient = Convert.ToDouble(Environment.GetEnvironmentVariable("RetryBackOffCofecient")),
            MaxRetryInterval = TimeSpan.FromMinutes(Convert.ToInt32(Environment.GetEnvironmentVariable("RetryMaxIntervalMinutes")))
        };

        [Deterministic]
        [FunctionName(nameof(MainThrottledOrchestrator))]
        public async Task MainThrottledOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context, ILogger log)
        {
            string[] arr = context.InstanceId.Split('|');
            string key = arr[0];
            ILogger logger = context.CreateReplaySafeLogger(log);
            EntityId id = new(nameof(EntityFunctions.LockByKey), key);
            bool didadd = false;
            string token = string.Empty;
            bool haslock = false;
            long ticks = Convert.ToInt64(arr[1]);
            EntityId globalcountid;

            try
            {
                while (true)
                {
                    using (await context.LockAsync(id))
                    {
                        Lock loc = await context.CallEntityAsync<Lock>(id, "getlock");

                        token = $"{key}/{loc.TicksStamp}";

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

                    DateTime deadline = context.CurrentUtcDateTime.Add(TimeSpan.FromSeconds(10));
                    await context.CreateTimer(deadline, CancellationToken.None);
                }

                string globalMaxConcurrent = "MaxConcurrentOutboundCalls";

                if (!int.TryParse(Environment.GetEnvironmentVariable(globalMaxConcurrent), out int maxparallel))
                {
                    maxparallel = 5;

                    logger.LogWarning($"Config setting '{globalMaxConcurrent}' not found, defaulting to 5 max concurrent.");
                }

                globalcountid = new(nameof(EntityFunctions.GlobalConcurrentCount), globalMaxConcurrent);

                int globalcount;

                while (true)
                {
                    using (await context.LockAsync(globalcountid))
                    {
                        globalcount = await context.CallEntityAsync<int>(globalcountid, "get");

                        if (globalcount >= maxparallel)
                        {
                            DateTime deadline = context.CurrentUtcDateTime.Add(TimeSpan.FromSeconds(10));
                            await context.CreateTimer(deadline, CancellationToken.None);

                            continue;
                        }

                        await context.CallEntityAsync(globalcountid, "add", globalcount + 1);

                        didadd = true;
                    }

                    logger.LogCritical("CONCURRENT ++++++++++++++++++++++++: " + (globalcount + 1));

                    break;
                }

                (string _, ticks) = await context.CallSubOrchestratorWithRetryAsync<(string, long)>(nameof(BlobProcessOrchestrator), retryOptions, key, token);

                logger.LogWarning("!!!!!!!!!!!!!!!<<<<<<< DONE >>>>>>>!!!!!!!!!!!!!!!   KEY: " + key);
            }
            catch (Exception ex)
            {
                // log error
                throw;
            }
            finally
            {
                if (haslock)
                {
                    if(didadd)
                    {
                        await context.CallEntityAsync<int>(globalcountid, "sub");
                    }

                    await context.CallEntityAsync(id, "lock", new Lock() { IsLocked = false, TicksStamp = ticks });
                }
            }
        }

        [Deterministic]
        [FunctionName(nameof(BlobProcessOrchestrator))]
        public async Task<(string, long)> BlobProcessOrchestrator([OrchestrationTrigger] IDurableOrchestrationContext context, ILogger log)
        {
            long lastticks = 0;
            string token;
            //string trace = "1";

            string globalMaxConcurrent = "MaxConcurrentOutboundCalls";

            try
            {
                ILogger logger = context.CreateReplaySafeLogger(log);

                if (!int.TryParse(Environment.GetEnvironmentVariable(globalMaxConcurrent), out int maxparallel))
                {
                    maxparallel = 5;

                    logger.LogWarning($"Config setting '{globalMaxConcurrent}' not found, defaulting to 5 max concurrent.");
                }

                token = context.GetInput<string>();

                (token, List<string> blobs, lastticks) = await context.CallActivityWithRetryAsync<(string, List<string>, long)>(nameof(BlobFunctions.GetBlobs), retryOptions, token);

                if (blobs.Count == 0)
                {
                    if (!string.IsNullOrEmpty(token))
                    {
                        context.ContinueAsNew(token);
                        //trace += "2";
                    }
                    //trace += "3";
                    return (token, lastticks);
                }

                string[] arr = blobs.Last().Split('/');

                await context.CallEntityAsync(new EntityId(nameof(EntityFunctions.LockByKey), arr[0]), "lock", new Lock() { IsLocked = true, TicksStamp = Convert.ToInt64(arr[1]) });

                foreach (string blob in blobs)
                {
                    // add another CallActivityWithRetryAsync here to get an authentication token if needed

                    await context.CallActivityWithRetryAsync(nameof(HttpCalls.CallExternalHttp), retryOptions, blob);

                    await context.CallActivityWithRetryAsync(nameof(BlobFunctions.DeleteBlob), retryOptions, blob);
                }

                //trace += "4";
                if (string.IsNullOrEmpty(token))
                {
                    //trace += "5";
                    return (token, lastticks);
                }

                //trace += "6";
                context.ContinueAsNew(token);
                //trace += "7";

                return (null, 0);
            }
            catch (Exception ex)
            {
                // log error
                throw;
            }
        }
    }
}