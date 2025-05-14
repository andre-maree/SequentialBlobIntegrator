using Azure.Identity;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: FunctionsStartup(typeof(SequentialBlobIntegrator.Startup))]
namespace SequentialBlobIntegrator
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            IConfiguration configuration = builder.GetContext().Configuration;
            string storageContainerName = configuration.GetValue<string>("AzureWebJobsStorage");

            builder.Services.AddHttpClient();

            builder.Services.AddAzureClients(async clientBuilder =>
            {
                // Register clients for each service
                clientBuilder.AddBlobServiceClient(storageContainerName);
                

                // Set a credential for all clients to use by default
                DefaultAzureCredential credential = new();
                clientBuilder.UseCredential(credential);

            });
        }
    }
}