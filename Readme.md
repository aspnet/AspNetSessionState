## Introduction
SessionStateModule is ASP.NET’s default session-state handler which retrieves session data and writes it to the session-state store. It already operates asynchronously when acquiring the request state, but it doesn’t support async read/write to the session-state store. In the .NET Framework 4.6.2 release, we introduced a new interface named ISessionStateModule to enable this scenario. You can find more details on [this blog post](https://blogs.msdn.microsoft.com/webdev/2016/09/29/introducing-the-asp-net-async-sessionstate-module/).

## How to build
1. Open a [VS developer command prompt](https://docs.microsoft.com/en-us/dotnet/framework/tools/developer-command-prompt-for-vs)
2. Run build.cmd. This will build Nuget package and run all the unit tests.
3. All the build artifacts will be under aspnetsessionstate\bin\Release\ folder.

## How to contribute
Information on contributing to this repo is in the [Contributing Guide](CONTRIBUTING.md).

## Replace the existing Session module in `web.config`

Before you can specify one of these custom providers. You need to remove the existing session state module from your web.config file. In addition, you must register the new module to take its place.

```
  <system.webServer>
    <modules>
      <!-- remove the existing Session state module -->
      <remove name="Session" />
      <add name="Session" preCondition="integratedMode" type="Microsoft.AspNet.SessionState.SessionStateModuleAsync, Microsoft.AspNet.SessionState.SessionStateModule, Version=1.1.0.0, Culture=neutral" />
    </modules>
  </system.webServer>
```

## Settings of the module and providers

+ #### Microsoft.AspNet.SessionState.SessionStateModule

1. appSetting *aspnet:RequestQueueLimitPerSession*

    *How to use* - Add ```<add key="aspnet:RequestQueueLimitPerSession" value="[int]"/>``` into web.config under appSettings section.
    
    *Description* - If multiple requests with same sessionid try to acquire sessionstate concurrently, asp.net only allows one request to get the sessionstate. This causes performance issues if there are too many requests with same sessionid and a request doesn't release sessionstate fast enough, as asp.net starts a timer for each of this request to acquire sessionstate every 0.5 sec by default. This is even worse, if out-proc sessionstate provider is used. Because this can potentially use most of the out-proc storage connection resources. With this setting, asp.net will ends the request after the number of concurrent requests with same sessionid reaches the configured number.

2. appSetting *aspnet:AllowConcurrentRequestsPerSession*
    
    *How to use* - Add ```<add key="aspnet:AllowConcurrentRequestsPerSession" value="[bool]"/>``` into web.config under appSettings section.
    
    *Description* - If multiple requests with same sessionid try to acquire sessionstate concurrently, asp.net only allows one request to get the sessionstate. With this setting, asp.net will allow multiple requests with same sessionid to acquire the sessionstate, but it doesn't guarantee thread safe of accessing sessionstate.

+ #### Microsoft.AspNet.SessionState.SqlSessionStateProviderAsync
    
    The settings of this provider is located in the following configuration section in web.config.
```
    <sessionState cookieless="false" regenerateExpiredSessionId="true" mode="Custom" customProvider="SqlSessionStateProviderAsync">
      <providers>
        <add name="SqlSessionStateProviderAsync" connectionStringName="DefaultConnection" 
             UseInMemoryTable="[true|false]" MaxRetryNumber="[int]" RetryInterval="[int]" ApplicationName="[string]"
          type="Microsoft.AspNet.SessionState.SqlSessionStateProviderAsync, Microsoft.AspNet.SessionState.SqlSessionStateProviderAsync, Version=1.1.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35"/>
      </providers>
```

1. *UseInMemoryTable* - Indicates whether to use Sql server 2016 In-Memory OLTP for sessionstate. You can find more details about using In-memory table for sessionstate [on this blog](https://blogs.msdn.microsoft.com/sqlcat/2016/10/26/how-bwin-is-using-sql-server-2016-in-memory-oltp-to-achieve-unprecedented-performance-and-scale/).

2. *MaxRetryNumber* - The maximum number of retrying executing sql query to read/write sessionstate data from/to Sql server. The default value is 10.

3. *RetryInterval* - The interval between the retry of executing sql query. The default value is 0.001 sec for in-memorytable mode. Otherwise the default value is 1 sec.

4. *ApplicationName* - If an application runs on multiple nodes, the same session is used if and only if the application name is identically specified on all nodes.

+ #### Microsoft.AspNet.SessionState.CosmosDBSessionStateProviderAsync

    The settings of this provider is located in the following configuration section in web.config.
```
<sessionState cookieless="false" regenerateExpiredSessionId="true" mode="Custom" customProvider="CosmosDBSessionStateProviderAsync">
      <providers>
        <add name="CosmosDBSessionStateProviderAsync" cosmosDBEndPointSettingKey="cosmosDBEndPointSetting" cosmosDBAuthKeySettingKey="cosmosDBAuthKeySetting"
          databaseId="[DataBaseId]" collectionId="[CollectionId]" offerThroughput="5000" connectionMode="Direct" connectionProtocol="Tcp" requestTimeout="5"
          maxConnectionLimit="50" maxRetryAttemptsOnThrottledRequests="10" maxRetryWaitTimeInSeconds="10" preferredLocations="" partitionKey="pKey"
          partitionNumUsedByProvider="*"
          type="Microsoft.AspNet.SessionState.CosmosDBSessionStateProviderAsync, Microsoft.AspNet.SessionState.CosmosDBSessionStateProviderAsync, Version=1.1.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35"/>
      </providers>
    </sessionState>
```

NOTE: For the best scalability, it is recommended to configure your CosmosDB provider with "wildcard" partitioning. For update-compatibility purposes, this is not the default. Please read about
*partitionKeyPath* and *partitionNumUsedByProvider* below.

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
