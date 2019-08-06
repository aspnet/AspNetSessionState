## Introduction
SessionStateModule is ASP.NET’s default session-state handler which retrieves session data and writes it to the session-state store. It already operates asynchronously when acquiring the request state, but it doesn’t support async read/write to the session-state store. In the .NET Framework 4.6.2 release, we introduced a new interface named ISessionStateModule to enable this scenario. You can find more details on [this blog post](https://blogs.msdn.microsoft.com/webdev/2016/09/29/introducing-the-asp-net-async-sessionstate-module/).

## How to build
1. Open a [VS developer command prompt](https://docs.microsoft.com/en-us/dotnet/framework/tools/developer-command-prompt-for-vs)
2. Run build.cmd. This will build Nuget package and run all the unit tests.
3. All the build artifacts will be under aspnetsessionstate\bin\Release\ folder.

## How to contribute
Information on contributing to this repo is in the [Contributing Guide](CONTRIBUTING.md).

## How to use
1. Update your web.config to remove the old session state module and register the new one:
```xml
  <system.webServer>
    <modules>
      <!-- remove the existing Session state module -->
      <remove name="Session" />
      <add name="Session" preCondition="integratedMode" type="Microsoft.AspNet.SessionState.SessionStateModuleAsync, Microsoft.AspNet.SessionState.SessionStateModule, Version=1.1.0.0, Culture=neutral" />
    </modules>
  </system.webServer>
```
2. Add one of the new providers to the `<sessionState>` section of your web.config:
```xml
  <sessionState cookieless="false" regenerateExpiredSessionId="true" mode="Custom" customProvider="YourProviderName">
    <providers>
      <add name="YourProviderName" [providerOptions] type="Provider, ProviderAssembly, Version=, Culture=neutral, PublicKeyToken=" />
    </providers>
  </sessionState>
```
The specific settings available for the new session state module and providers are detailed in their respective doc pages.

## Module and Providers contained here
- [Microsoft.AspNet.SessionState.SessionStateModule](docs/SessionStateModule.md)
- [Microsoft.AspNet.SessionState.SqlSessionStateProviderAsync](docs/SqlSessionStateProviderAsync.md)
- [Microsoft.AspNet.SessionState.CosmosDBSessionStateProviderAsync](docs/CosmosDBSessionStateProviderAsync.md)

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
