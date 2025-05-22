using Azure.Identity;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using System;

[assembly: FunctionsStartup(typeof(SequentialBlobIntegrator.Startup))]
namespace SequentialBlobIntegrator
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            IConfiguration configuration = builder.GetContext().Configuration;
            string storageContainerName = configuration.GetValue<string>("AzureWebJobsStorage");

            builder.Services.AddHttpClient("HttpBinHttpClient", client => { client.BaseAddress = new Uri(configuration.GetValue<string>("HttpBinBaseUrl")); });
            builder.Services.AddAzureClients(clientBuilder =>
            {
                // Register clients for each service
                clientBuilder.AddBlobServiceClient(storageContainerName);
                

                // Set a credential for all clients to use by default
                DefaultAzureCredential credential = new();
                clientBuilder.UseCredential(credential);

            }); 
            
            builder.Services.AddHttpClient("GitHub", httpClient =>
            {
                httpClient.BaseAddress = new Uri("https://api.github.com/");

                // using Microsoft.Net.Http.Headers;
                // The GitHub API requires two headers.
                httpClient.DefaultRequestHeaders.Add(
                    HeaderNames.Accept, "application/vnd.github.v3+json");
                httpClient.DefaultRequestHeaders.Add(
                    HeaderNames.UserAgent, "HttpRequestsSample");
            });
        }
    }
}