// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNet.SessionState.AsyncProviders.SqlSessionState.Entities
{
    using System;
    using System.Configuration;
    using System.Configuration.Provider;
    using System.Data.Entity;
    using System.Data.Entity.Infrastructure;
    using System.Globalization;
    using Resources;

    internal static class ModelHelper
    {
        private static bool s_sessionInitialized = false;
        private static bool s_dbInitialized = false;
        private static readonly object s_lock = new object();

        public static SessionContext CreateSessionContext(ConnectionStringSettings setting)
        {
            if (!s_dbInitialized)
            {
                lock (s_lock)
                {
                    if (!s_dbInitialized)
                    {
                        Database.SetInitializer<SessionContext>(null);
                        s_dbInitialized = true;
                    }
                }
            }
            var db = new SessionContext(setting.Name);
            if (!s_sessionInitialized)
            {
                lock(s_lock)
                {
                    if (!s_sessionInitialized)
                    {
                        EnsureDatabaseCreated(db);
                        ExecuteSql(db, "CREATE INDEX IX_Sessions_Expires ON Sessions (Expires)");
                        s_sessionInitialized = true;
                    }
                }
            }
            return db;
        }

        public static int ExecuteSql(DbContext db, string sql)
        {
            // break the sql up into ; and exec each one by itself
            string[] cmds = sql.Split(new char[] { ';' });
            int results = 0;
            foreach (string c in cmds)
            {
                try
                {
                    results += db.Database.ExecuteSqlCommand(c + ";");
                }
                catch
                {
                    // error roll back
                    return -1;
                }
            }
            return results;
        }

        internal static ConnectionStringSettings GetConnectionString(string connectionstringName)
        {
            if (string.IsNullOrEmpty(connectionstringName))
            {
                throw new ProviderException(SR.Connection_name_not_specified);
            }
            ConnectionStringSettings conn = ConfigurationManager.ConnectionStrings[connectionstringName];
            if (conn == null)
            {
                throw new ProviderException(
                    String.Format(CultureInfo.CurrentCulture, SR.Connection_string_not_found, connectionstringName));
            }
            return conn;
        }

        private static void EnsureDatabaseCreated(DbContext db)
        {
            // If database already exists, try to inject the new tables and ignore any failures
            // since subsequent runs the tables will already exist
            if (db.Database.Exists())
            {
                string script = ((IObjectContextAdapter)db).ObjectContext.CreateDatabaseScript();
                ExecuteSql(db, script);
            }
            else {
                db.Database.Create();
            }
        }
    }
}
