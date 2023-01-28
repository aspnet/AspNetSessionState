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

<a name="updates"></a>
## V1.2 Updates:
  * :warning: ***Breaking Change*** - CosmosDB partition-related parameters are ignored. All containers use `/id` as the partition path now. Using an existing container with a different partition key path will result in exceptions.
    > The original design around partition use in the CosmosDB provider was influenced by experience with the older SQL partition paradigms. There was an effort to enable them for scalability, but keep them reasonable for managability. In reality, CosmosDB encourages the use of as many "logical" partitions as can be used so long as they make sense. The complexity of managing and scaling is all handled magically by CosmosDB.
    >
    > The most logical partition field for session state is the session ID. The CosmosDB provider has been updated to alway use `"/id"` as the partition key path with the full session ID as the partition value. Pre-existing containers that use a different partition key path (which is any that opted into using partitions previously) will need to migrate to a container that uses `"/id"` as the partition key path. The data is all still good - although, the old partition key path can be dropped when migrating. There is unfortunately no way to simply update the partition key path on an existing container right now. [This blog post](https://devblogs.microsoft.com/cosmosdb/how-to-change-your-partition-key/) is a guide for migrating to a new container with the correct partition key path.
  * CosmosDB `collectionId` is now `containerId` in keeping with the updated terminology from the CosmosDB offering. Please use the updated parameter name when configuring your provider. (The old name will continue to work just the same.)
  * CosmosDB `connectionProtocol` is obsolete. It will not cause errors to have it in configuration, but it is ignored. The current [CosmosDB SDK chooses the protocol based on connection mode](https://learn.microsoft.com/en-us/azure/cosmos-db/nosql/sdk-connection-modes).
