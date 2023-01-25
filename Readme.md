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
