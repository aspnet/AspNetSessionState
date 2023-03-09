# Microsoft.AspNet.SessionState.SessionStateModule
SessionStateModule is ASP.NET’s default session-state handler which retrieves session data and writes it to the session-state store. It already operates asynchronously when acquiring the request state, but it doesn’t support async read/write to the session-state store. In the .NET Framework 4.6.2 release, we introduced a new interface named ISessionStateModule to enable this scenario. You can find more details on [this blog post](https://blogs.msdn.microsoft.com/webdev/2016/09/29/introducing-the-asp-net-async-sessionstate-module/).

Before you can specify one of these custom providers. You need to remove the existing session state module from your web.config file. In addition, you must register the new module to take its place.

```xml
  <system.webServer>
    <modules>
      <!-- remove the existing Session state module -->
      <remove name="Session" />
      <add name="Session" preCondition="integratedMode,managedHandler" type="Microsoft.AspNet.SessionState.SessionStateModuleAsync, Microsoft.AspNet.SessionState.SessionStateModule, Version=2.0.0.0, Culture=neutral" />
    </modules>
  </system.webServer>
```

## Settings of Microsoft.AspNet.SessionState.SessionStateModule

1. appSetting *aspnet:RequestQueueLimitPerSession*

    *How to use* - Add ```<add key="aspnet:RequestQueueLimitPerSession" value="[int]"/>``` into web.config under appSettings section.
    
    *Description* - If multiple requests with same sessionid try to acquire sessionstate concurrently, asp.net only allows one request to get the sessionstate. This causes performance issues if there are too many requests with same sessionid and a request doesn't release sessionstate fast enough, as asp.net starts a timer for each of this request to acquire sessionstate every 0.5 sec by default. This is even worse, if out-proc sessionstate provider is used. Because this can potentially use most of the out-proc storage connection resources. With this setting, asp.net will ends the request after the number of concurrent requests with same sessionid reaches the configured number.

2. appSetting *aspnet:AllowConcurrentRequestsPerSession*
    
    *How to use* - Add ```<add key="aspnet:AllowConcurrentRequestsPerSession" value="[bool]"/>``` into web.config under appSettings section.
    
    *Description* - If multiple requests with same sessionid try to acquire sessionstate concurrently, asp.net only allows one request to get the sessionstate. With this setting, asp.net will allow multiple requests with same sessionid to acquire the sessionstate, but it doesn't guarantee thread safe of accessing sessionstate.
