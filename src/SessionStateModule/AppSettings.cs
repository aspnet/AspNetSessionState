// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See the License.txt file in the project root for full license information.

namespace Microsoft.AspNet.SessionState
{
    using System.Collections.Specialized;
    using System.Web.Configuration;

    static class AppSettings
    {
        private static volatile bool _settingsInitialized;
        private static object _lock = new object();

        private static void LoadSettings(NameValueCollection appSettings)
        {
            //
            // RequestQueueLimitPerSession
            //
            string requestQueueLimit = appSettings["aspnet:RequestQueueLimitPerSession"];

            if (!int.TryParse(requestQueueLimit, out _requestQueueLimitPerSession) || _requestQueueLimitPerSession < 0)
            {
                _requestQueueLimitPerSession = DefaultRequestQueueLimitPerSession;
            }

            //
            // AllowConcurrentRequests
            //
            string allowConcurrentRequestPerSession = appSettings["aspnet:AllowConcurrentRequestsPerSession"];
            bool.TryParse(allowConcurrentRequestPerSession, out _allowConcurrentRequestsPerSession);
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
        // RequestQueueLimitPerSession
        // Limit of queued requests per session
        //
        public const int DefaultRequestQueueLimitPerSession = 50;
        private static int _requestQueueLimitPerSession = DefaultRequestQueueLimitPerSession;

        public static int RequestQueueLimitPerSession
        {
            get
            {
                EnsureSettingsLoaded();
                return _requestQueueLimitPerSession;
            }
        }

        private static bool _allowConcurrentRequestsPerSession = false;
        public static bool AllowConcurrentRequestsPerSession
        {
            get
            {
                EnsureSettingsLoaded();
                return _allowConcurrentRequestsPerSession;
            }
        }
    }
}
