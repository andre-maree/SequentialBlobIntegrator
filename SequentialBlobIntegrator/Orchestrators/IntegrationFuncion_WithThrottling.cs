using SequentialBlobIntegrator.Models;

namespace SequentialBlobIntegrator
{ 
    public class IntegrationFuncion_WithThrottling
    {
        [Deterministic]
        [FunctionName(nameof(MainThrottledOrchestrator))]
        public static async Task MainThrottledOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context, ILogger log)
        {
            string[] arr = context.InstanceId.Split('|');
            string key = arr[0];
            ILogger logger = context.CreateReplaySafeLogger(log);
            EntityId id = new(nameof(EntityFunctions.LockByKey), key);

            string token = string.Empty;
            bool haslock = false;
            long ticks = Convert.ToInt64(arr[1]);

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

                    DateTime deadline = context.CurrentUtcDateTime.Add(TimeSpan.FromSeconds(5));
                    await context.CreateTimer(deadline, CancellationToken.None);
                }

                (string _, ticks) = await context.CallSubOrchestratorAsync<(string, long)>(nameof(BlobProcessOrchestrator), key, token);

                logger.LogWarning(context.CurrentUtcDateTime.ToString());
            }
            catch (Exception ex)
            {
                int r = 0;
            }
            finally
            {
                if (haslock)
                {
                    await context.CallEntityAsync(id, "lock", new Lock() { IsLocked = false, TicksStamp = ticks });
                }
            }
        }

        [Deterministic]
        [FunctionName(nameof(BlobProcessOrchestrator))]
        public async Task<(string, long)> BlobProcessOrchestrator([OrchestrationTrigger] IDurableOrchestrationContext context, ILogger log)
        {
            bool didadd = false;
            bool didsubtract = false;
            long lastticks = 0;
            string token;
            //string trace = "1";

            string globalMaxConcurrent = "MaxConcurrentOutboundCalls";

            EntityId globalcountid;

            try
            {
                ILogger logger = context.CreateReplaySafeLogger(log);

                if (!int.TryParse(Environment.GetEnvironmentVariable(globalMaxConcurrent), out int maxparallel))
                {
                    maxparallel = 5;
                    logger.LogWarning($"Config setting '{globalMaxConcurrent}' not found, defaulting to 5 max concurrent.");
                }

                globalcountid = new(nameof(EntityFunctions.GlobalConcurrentCount), globalMaxConcurrent);

                token = context.GetInput<string>();

                (token, List<string> blobs, lastticks) = await context.CallActivityAsync<(string, List<string>, long)>(nameof(BlobFunctions.GetBlobs), token);

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

                foreach (string blob in blobs)
                {
                    int globalcount;

                    while (true)
                    {
                        using (await context.LockAsync(globalcountid))
                        {
                            globalcount = await context.CallEntityAsync<int>(globalcountid, "get");

                            if (globalcount >= maxparallel)
                            {
                                DateTime deadline = context.CurrentUtcDateTime.Add(TimeSpan.FromSeconds(5));
                                await context.CreateTimer(deadline, CancellationToken.None);

                                continue;
                            }

                            await context.CallEntityAsync(globalcountid, "add");
                            didadd = true;
                        }

                        logger.LogCritical("CONCURRENT ++++++++++++++++++++++++: " + (globalcount + 1));

                        await context.CallActivityAsync(nameof(BlobFunctions.CallExternalHttp), blob);

                        DateTime deadline2 = context.CurrentUtcDateTime.Add(TimeSpan.FromSeconds(1));
                        await context.CreateTimer(deadline2, CancellationToken.None);

                        await context.CallEntityAsync<int>(globalcountid, "sub");
                        didsubtract = true;

                        await context.CallActivityAsync(nameof(BlobFunctions.DeleteBlob), blob);

                        break;
                    }
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
                if (didadd && !didsubtract)
                {
                    await context.CallEntityAsync<int>(globalcountid, "sub");
                }

                throw;
            }
        }
    }
}