# Microsoft.AspNet.SessionState.CosmosDBSessionStateProviderAsync
In .Net 4.6.2, asp.net enables developer plug in async version of SessionState module which is a good fit for the non-in-memory SessionState data store. This SessionState provider uses SQL Server as the data store and leverages async database operation to provide better scability.

Before you can specify this new async providers, you need to setup the new async SessionStateModule as [described here](https://github.com/aspnet/AspNetSessionState/blob/main/docs/SessionStateModule.md).

Then, register your new provider like so:
```xml
  <sessionState cookieless="false" regenerateExpiredSessionId="true" mode="Custom" customProvider="CosmosDBSessionStateProviderAsync">
    <providers>
      <add name="CosmosDBSessionStateProviderAsync" cosmosDBEndPointSettingKey="cosmosDBEndPointSetting" cosmosDBAuthKeySettingKey="cosmosDBAuthKeySetting"
          databaseId="[DataBaseId]" collectionId="[CollectionId]" offerThroughput="5000" connectionMode="Direct" connectionProtocol="Tcp" requestTimeout="5"
          maxConnectionLimit="50" maxRetryAttemptsOnThrottledRequests="10" maxRetryWaitTimeInSeconds="10" preferredLocations="" partitionKeyPath="pKey"
          partitionNumUsedByProvider="*"
          type="Microsoft.AspNet.SessionState.CosmosDBSessionStateProviderAsync, Microsoft.AspNet.SessionState.CosmosDBSessionStateProviderAsync, Version=1.1.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35"/>
    </providers>
  </sessionState>
```
> **Note**
> For the best scalability, it is recommended to configure your CosmosDB provider with "wildcard" partitioning. For update-compatibility purposes, this is not the default. Please read about *partitionKeyPath* and *partitionNumUsedByProvider* below.

## Settings for Microsoft.AspNet.SessionState.CosmosDBSessionStateProviderAsync
1. *cosmosDBEndPointSettingKey* - The appsetting key name which points to a CosmosDB end point

2. *cosmosDBAuthKeySettingKey* - The appsetting key name which points to a CosmosDB auth key

3. *offerThroughput* - The offer throughput provisioned for a collection in measurement of Requests-per-Unit in the Azure DocumentDB database service. If the collection provided doesn't exist, the provider will create a collection with this offerThroughput.

4. *connectionMode* - Direct | Gateway

5. *connectionProtocol* - Https | Tcp

6. *requestTimeout* - The request timeout in seconds when connecting to the Azure DocumentDB database service.

7. *maxConnectionLimit* - maximum number of concurrent connections allowed for the target service endpoint in the Azure DocumentDB database service.

8. *maxRetryAttemptsOnThrottledRequests* - the maximum number of retries in the case where the request fails because the Azure DocumentDB database service has applied rate limiting on the client.

9. *maxRetryWaitTimeInSeconds* - The maximum retry time in seconds for the Azure DocumentDB database service.

10. *preferredLocations* - Sets the preferred locations(regions) for geo-replicated database accounts in the Azure DocumentDB database service. Use ';' to split multiple locations. e.g. "East US;South Central US;North Europe"

11. *partitionKeyPath* - The name of the key to use for logically partitioning the collection. This key name should be different from 'id' unless "wildcard" partitioning is being used.

12. *partitionNumUsedByProvider* - The number of partition can be used for sessionstate. This was designed with the thought that multiple Cosmos partitions would be an extra cost. CosmosDB as it stands today encourages as many diverse logical partitions as you can imagine, as more partitions allow for better horizontal scaling. Setting this to an integer value will effectively reduce the partition count to 32 or less, even if the specified value is much greater. This is a result of how session ID's were translated to partition ID's by this provider. ***It is now recommended to specify "\*" for this option if possible.*** This will reuse the full session ID for partitioning, allowing Cosmos maximum ability for horizontal scaling.
