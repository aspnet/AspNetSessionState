# Microsoft.AspNet.SessionState.CosmosDBSessionStateProviderAsync
In .Net 4.6.2, asp.net enables developer plug in async version of SessionState module which is a good fit for the non-in-memory SessionState data store. This SessionState provider uses SQL Server as the data store and leverages async database operation to provide better scability.

Before you can specify this new async providers, you need to setup the new async SessionStateModule as [described here](https://github.com/aspnet/AspNetSessionState/blob/main/docs/SessionStateModule.md).

Then, register your new provider like so:
```xml
  <sessionState cookieless="false" regenerateExpiredSessionId="true" mode="Custom" customProvider="CosmosDBSessionStateProviderAsync">
    <providers>
      <add name="CosmosDBSessionStateProviderAsync" cosmosDBEndPointSettingKey="cosmosDBEndPointSetting" cosmosDBAuthKeySettingKey="cosmosDBAuthKeySetting"
          databaseId="[DataBaseId]" collectionId="[CollectionId]" offerThroughput="5000" connectionMode="Direct" requestTimeout="5" skipKeepAliveWhenUnused="false"
          maxConnectionLimit="50" maxRetryAttemptsOnThrottledRequests="10" maxRetryWaitTimeInSeconds="10" consistencyLevel="Session" preferredLocations=""
          type="Microsoft.AspNet.SessionState.CosmosDBSessionStateProviderAsync, Microsoft.AspNet.SessionState.CosmosDBSessionStateProviderAsync, Version=2.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35"/>
    </providers>
  </sessionState>
```
> **Note**
> For the best scalability, it is recommended to configure your CosmosDB provider with "wildcard" partitioning. For update-compatibility purposes, this is not the default. Please read about *partitionKeyPath* and *partitionNumUsedByProvider* below.

## Settings for Microsoft.AspNet.SessionState.CosmosDBSessionStateProviderAsync
1. *cosmosDBEndPointSettingKey* - The appsetting key name which points to a CosmosDB end point

2. *cosmosDBAuthKeySettingKey* - The appsetting key name which points to a CosmosDB auth key

3. *offerThroughput* - The offer throughput provisioned for a collection in measurement of Requests-per-Unit in the Azure DocumentDB database service. If the collection provided doesn't exist, the provider will create a collection with this offerThroughput. If set to "0", collection will be set to use the default throughput of the database.

4. *connectionMode* - Direct | Gateway

5. *requestTimeout* - The request timeout in seconds when connecting to the Azure DocumentDB database service.

6. *skipKeepAliveWhenUnused* - This setting will skip the call to update expiration time on requests that did not read or write session state. The default is "false" to maintain compatibility with previous behavior. But certain applications (like MVC) where there can be an abundance of requests processed that never even look at session state could benefit from setting this to "true" to reduce the use of and contention within the session state store. Setting this to "true" does mean that a session needs to be used (not necessarily updated, but at least requested/queried) to stay alive.

7. *maxConnectionLimit* - maximum number of concurrent connections allowed for the target service endpoint in the Azure DocumentDB database service.

8. *maxRetryAttemptsOnThrottledRequests* - the maximum number of retries in the case where the request fails because the Azure DocumentDB database service has applied rate limiting on the client.

9. *maxRetryWaitTimeInSeconds* - The maximum retry time in seconds for the Azure DocumentDB database service.

10. *consistencyLevel* - The [Consistency Level](https://learn.microsoft.com/en-us/azure/cosmos-db/consistency-levels) to use with the CosmosClient. Default is the Cosmos SDK default, which is currently 'Session'.

11. *preferredLocations* - Sets the preferred locations(regions) for geo-replicated database accounts in the Azure DocumentDB database service. Use ';' to split multiple locations. e.g. "East US;South Central US;North Europe"
