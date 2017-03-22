// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNet.SessionState
{
    using System.Collections.Specialized;
    using System.Web.Configuration;

    internal static class AppSettings
    {
        private static volatile bool _settingsInitialized;
        private static object _lock = new object();

        private static void LoadSettings(NameValueCollection appSettings)
        {
            //
            // RequestQueueLimitPerSession
            //
            string inMemoryTable = appSettings["aspnet:SqlSessionUseInMemoryTable"];

            bool.TryParse(inMemoryTable, out _useInMemoryTable);
        }

        private static void EnsureSettingsLoaded()
        {
            if (_settingsInitialized)
            {
                return;
            }

            lock (_lock)
            {
                if (!_settingsInitialized)
                {
                    try
                    {
                        LoadSettings(WebConfigurationManager.AppSettings);
                    }
                    finally
                    {
                        _settingsInitialized = true;
                    }
                }
            }
        }

        //
        // If using sql In-MemoryTable        
        //
        private static bool _useInMemoryTable = false;

        public static bool UserInMemoryTable
        {
            get
            {
                EnsureSettingsLoaded();
                return _useInMemoryTable;
            }
        }
    }
}
