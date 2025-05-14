# Sequential Blob Integrator Function App

This C# project can be used for integration. Sequential updates are enforced by saving payloads to blobs within transactions. No ServiceBus queues are needed (for first-in-first-out), as long as the blob is saved with a ticks timestamp for its name within an originating transaction. For example, when outbound integration is needed for D365 CRM, a plugin can run with a synchronous post-operation that saves a blob within the transaction for the row update or create. This means that the blobs will be ordered correctly sequentially by name. All that the the D365 plugin must do is to save the blob with the payload with the correct name. An Azure Function blob trigger will start a Durable Function that will process the blobs sequentially (by the set key).

This will also work for updates in MS SQL for updates that run within transactions. For a MS SQL solution, use a in-line SQL with a transaction or call a stored procedure with a transaction.

Functionality include:

- Sequential outbound updates per key. The key should be the row id.
- Only 1 outbound call will execute concurrently per key, in the correct sequential order.
- Outbound calls will execute concurrently across row keys.
- Outbound throttling: Outbound calls executing concurrently across different keys can be limited by how many execute concurrently. This is to make sure the outbound calls do not overwhelm the destination service or system.

To get started locally, run the function app and call http://localhost:7161/testFunction1_HttpStart. This will save some blobs and execute them sequentially and call the test external endpoint https://httpbin.org/get.

local.settings.json Config:

- "Container": "integration-test", // set the blob container to use
- "MaxConcurrentOutboundCalls": "4" // max concurrent outbound calls

Use Azure Storage Explorer and Azurerite for local development. Azure Storage Explorer can be downloaded, and Azurite should be included within Visual Sudio.
