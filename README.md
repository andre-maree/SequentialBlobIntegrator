# Sequential Blob Integration Function App

This C# project can be used for integration. Sequential operations (creates, updates, and deletes) are enforced by uploading json payloads to blobs within transactions. No ServiceBus queues are needed (for first-in-first-out), as long as the blob is saved with a ticks timestamp for its name within an originating transaction. For example, when outbound integration is needed for D365 CRM, a plugin can run with a synchronous post-operation that saves a blob within the transaction for the row create, update, or delete. This means that the blobs will be ordered correctly sequentially by name. All that the the D365 plugin must do is to upload the blob with the payload with the correct name: {key}/{ticks}. An Azure Function blob trigger will start a Durable Function that will process the blobs sequentially per key.

This will also work for creates, updates, and deletes in MS SQL by saving the blobs within transactions. For a MS SQL solution, use a code based transaction and call a stored procedure or in-line sql. If a sequential processing order is not required, then no transaction is needed.

Functionality include:

- Sequential outbound updates per key. The key should be the row id.
- Only 1 outbound call will execute concurrently per key, in the correct sequential order.
- Outbound calls will execute concurrently across row keys.
- Outbound throttling: Outbound calls executing concurrently across different keys can be limited by how many execute concurrently. This is to make sure the outbound calls do not overwhelm the destination service or system.

To get started locally, run the function app and call http://localhost:7161/testFunction1_HttpStart. This will save some blobs and execute them sequentially and call the test external endpoint https://httpbin.org/post. This endpoint is good for testing, because it will sometimes return with a failed http status code, and this is good to test that the retries are working.

local.settings.json Config:
```json
"Container": "integration-test", // set the blob container to use
"MaxConcurrentOutboundCalls": "5", // max concurrent outbound calls
"RetryMaxIntervalMinutes": "5",
"RetryFirstIntervalSeconds": "5",
"RetryMaxIntervals": "1000",
"RetryBackOffCofecient": "1.125",
"BlobBatchSize": "5000"
```

Use Azure Storage Explorer and Azurerite for local development. Azure Storage Explorer can be downloaded, and Azurite should be included within Visual Sudio.

Simply call [POST] http://localhost:7161/CreateIntegrationInstance with the following object model (included in the Models class). Look at the TestFunction class how to save the blobs with the needed json payload:

```csharp
public class IntegrationPayload()
{
    public string Key { get; set; }
    public long TicksStamp { get; set; }
    public IntegrationHttpRequest IntegrationHttpRequest { get; set; }

}
public class IntegrationHttpRequest
{
    public string Url { get; set; }
    public int Timeout { get; set; } = 15;
    public Dictionary<string, string[]> Headers { get; set; }
    public string HttpMethod { get; set; }
    public string Content { get; set; }
    public bool PollIf202 { get; set; }
}
```
