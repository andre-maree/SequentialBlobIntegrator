using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs;
using SequentialBlobIntegrator.Models;

namespace SequentialBlobIntegrator
{
    public class EntityFunctions
    {
        [FunctionName(nameof(GlobalConcurrentCount))]
        public static void GlobalConcurrentCount([EntityTrigger] IDurableEntityContext ctx)
        {
            switch (ctx.OperationName.ToLowerInvariant())
            {
                case "add":

                    //int count = ctx.GetInput<int>();
                    int c = ctx.GetState<int>();
                    c = c + 1;
                    ctx.SetState(c);

                    break;

                case "sub":

                    int j = ctx.GetState<int>();
                    j = j - 1;

                    ctx.SetState(j);

                    break;

                case "get":

                    int state = ctx.GetState<int>();
                    ctx.Return(state);

                    break;
            }
        }

        [FunctionName(nameof(LockByKey))]
        public static void LockByKey([EntityTrigger] IDurableEntityContext ctx)
        {
            switch (ctx.OperationName.ToLowerInvariant())
            {
                case "lock":

                    Lock loc = ctx.GetInput<Lock>();

                    ctx.SetState(loc);

                    break;

                case "getlock":

                    Lock loc2 = ctx.GetState<Lock>();

                    loc2 ??= new Lock();

                    ctx.Return(loc2);

                    break;
            }
        }
    }
}