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

                    int i = ctx.GetState<int>();
                    i = i + 1;

                    ctx.SetState(i);

                    break;

                case "sub":

                    int j = ctx.GetState<int>();
                    j = j - 1;

                    ctx.SetState(j);

                    break;

                case "get":

                    ctx.Return(ctx.GetState<int>());

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