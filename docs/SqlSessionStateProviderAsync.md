# Microsoft.AspNet.SessionState.SqlSessionStateProviderAsync
In .Net 4.6.2, asp.net enables developer plug in async version of SessionState module which is a good fit for the non-in-memory SessionState data store. This SessionState provider uses SQL Server as the data store and leverages async database operation to provide better scability.

Before you can specify this new async providers, you need to setup the new async SessionStateModule as [described here](https://github.com/aspnet/AspNetSessionState/blob/main/docs/SessionStateModule.md).

Then, register your new provider like so:
```xml
  <sessionState cookieless="false" regenerateExpiredSessionId="true" mode="Custom" customProvider="SqlSessionStateProviderAsync">
    <providers>
      <add name="SqlSessionStateProviderAsync" connectionStringName="DefaultConnection" UseInMemoryTable="[true|false]" MaxRetryNumber="[int]" RetryInterval="[int]" type="Microsoft.AspNet.SessionState.SqlSessionStateProviderAsync, Microsoft.AspNet.SessionState.SqlSessionStateProviderAsync, Version=1.1.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35"/>
    </providers>
  </sessionState>
```

## Settings for Microsoft.AspNet.SessionState.SqlSessionStateProviderAsync
1. *UseInMemoryTable* - Indicates whether to use Sql server 2016 In-Memory OLTP for sessionstate. You can find more details about using In-memory table for sessionstate [on this blog](https://blogs.msdn.microsoft.com/sqlcat/2016/10/26/how-bwin-is-using-sql-server-2016-in-memory-oltp-to-achieve-unprecedented-performance-and-scale/).

2. *MaxRetryNumber* - The maximum number of retrying executing sql query to read/write sessionstate data from/to Sql server. The default value is 10.

3. *RetryInterval* - The interval between the retry of executing sql query. The default value is 0.001 sec for in-memorytable mode. Otherwise the default value is 1 sec.
