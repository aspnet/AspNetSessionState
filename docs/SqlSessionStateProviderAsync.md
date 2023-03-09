# Microsoft.AspNet.SessionState.SqlSessionStateProviderAsync
In .Net 4.6.2, asp.net enables developer plug in async version of SessionState module which is a good fit for the non-in-memory SessionState data store. This SessionState provider uses SQL Server as the data store and leverages async database operation to provide better scability.

Before you can specify this new async providers, you need to setup the new async SessionStateModule as [described here](https://github.com/aspnet/AspNetSessionState/blob/main/docs/SessionStateModule.md).

Then, register your new provider like so:
```xml
  <sessionState cookieless="false" regenerateExpiredSessionId="true" mode="Custom" customProvider="SqlSessionStateProviderAsync">
    <providers>
      <add name="SqlSessionStateProviderAsync" connectionStringName="DefaultConnection" sessionTableName="[string]"
          repositoryType="[SqlServer|InMemory|InMemoryDurable|FrameworkCompat]"
          maxRetryNumber="[int]" retryInterval="[int]" skipKeepAliveWhenUnused="false"
          type="Microsoft.AspNet.SessionState.SqlSessionStateProviderAsync, Microsoft.AspNet.SessionState.SqlSessionStateProviderAsync, Version=2.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35"/>
    </providers>
  </sessionState>
```

## A Note About Tables and Data Durability
The old in-box SQL provider allowed for applications to choose between three data configurations by using the `-sstype` argument to `aspnet_regsql.exe`. Those types were <u>*p*</u>ermanent, <u>*t*</u>emporary, or <u>*c*</u>ustom. The difference between all three is simply the database and table name used by `aspnet_regsql.exe` and the application at runtime.
 * With the permanent option, session state would be stored in a hard-coded well-known table name in the database specified by the connection string. The table schema and data are "permanent" in this setup because they survive SQL server reboot.
 * With the temporary option, session state would be stored in a hard-coded well-known table name in the "tempdb" database on the SQL Server specified in the connection string. The "tempdb" database gets cleared upon SQL server reboot, so the data is not kept. The table schema and stored procedures are also cleared in this scenario, but there is a startup procedure that gets registered to re-create session state tables and stored procedures.
 * With the custom option, session state would be stored in a table name given by the developer/administrator. The stored procedures created in the database work against that custom table name.

 With this new provider, `aspnet_regsql.exe` is no longer required. (Although, compatibility with stores created by 'aspnet_regsql.exe' can be found when using 'repositoryType=FrameworkCompat'.) When using a `SqlServer` 'repositoryType' the provider will automatically create tables and stored procedures if they don't already exist. By default, that table name is "hard-coded and well-known" - though it has changed from previous versions to avoid inadvertent compatibility problems. You can change that table name by using the 'sessionTableName' attribute on the provider. Whether or not data is 'temporary' or 'permanent' in this type of repository depends entirely on the connection string used. If the connection string indicates using "tempdb", then the data will be temporary. If it indicates a non-temporary initial database, then the data will survive SQL reboots.

 When using a Memory-Optimized 'repositoryType' however, data durability is determined by the optimized table schema. Thus, the provider needs to know what settings to apply to any table it creates. If you want permanent data that survives SQL Server reboots, you must use `InMemoryDurable`.

## Settings for Microsoft.AspNet.SessionState.SqlSessionStateProviderAsync
1. *repositoryType* - One of four values. The default is 'FrameworkCompat' for compatibility reasons - unless the deprecated 'useInMemoryTable' is also set to true, then the default repository type becomes 'InMemory'.
    * `SqlServer` - Use this option to use a regular SQL server configuration for a fresh deployment. This configuration will create a session table and associated stored procedures if they don't already exist. (*Note* - the session table expected/created in by this repository type uses a different data type for storing state, and is thus incompatible with the 1.1 release of this provider.)
    * `InMemory` - Use this option to leverage "[In-Memory Optimized Tables](https://learn.microsoft.com/en-us/sql/relational-databases/in-memory-oltp/introduction-to-memory-optimized-tables?view=sql-server-ver16)" with "[Natively Compiled Stored Procedures](https://learn.microsoft.com/en-us/sql/relational-databases/in-memory-oltp/a-guide-to-query-processing-for-memory-optimized-tables?view=sql-server-ver16)". New in version 2.0, we create natively compiled stored procedures to go along with the memory-optimized table for an additional performance boost. (V1.1 did not use stored procedures at all.) Tables are durable, but the data is not (`SCHEMA_ONLY`) and will be lost on SQL Server restarts.
    * `InMemoryDurable` - The same as above, except with a `SCHEMA_AND_DATA` durable table so session data survives a SQL Server restart.
    * `FrameworkCompat` - This mode was introduced to use existing session state databases that were provisioned by `aspnet_regsql.exe`. As such, it does not create any new tables or stored procedures. It does leverage the same stored procedures that are used by the in-box SQL Session State provider.
    
      This compat configuration ***also*** handles going against a database that was previously configured by V1.1 of these providers, since the current table schema is not fully compatible with the 1.1 table schema. When working against a 1.1-deployed session table, this repository continues to use raw SQL statements instead of stored procedures just like the V1.1 provider did.
      
      The provider automatically decides between Framework and V1.1 compat modes in this configuration.

2. *sessionTableName* - The provider now allows the flexibility to use a particular table name for storing session instead of always using the hard-coded default.

3. *maxRetryNumber* - The maximum number of retrying executing sql query to read/write sessionstate data from/to Sql server. The default value is 10.

4. *retryInterval* - The interval between the retry of executing sql query. The default value is 0.001 sec for in-memorytable mode. Otherwise the default value is 1 sec.

5. *skipKeepAliveWhenUnused* - This setting will skip the call to update expiration time on requests that did not read or write session state. The default is "false" to maintain compatibility with previous behavior. But certain applications (like MVC) where there can be an abundance of requests processed that never even look at session state could benefit from setting this to "true" to reduce the use of and contention within the session state store. Setting this to "true" does mean that a session needs to be used (not necessarily updated, but at least requested/queried) to stay alive.

6. **[Deprecated]** *useInMemoryTable* - In the absence of a value for `repositoryType`, this setting will be used to determine whether to use Sql server 2016 In-Memory OLTP for sessionstate. However, if `repositoryType` is specified, that setting takes priority. You can find more details about using In-memory table for sessionstate [on this blog](https://blogs.msdn.microsoft.com/sqlcat/2016/10/26/how-bwin-is-using-sql-server-2016-in-memory-oltp-to-achieve-unprecedented-performance-and-scale/).
