using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Configuration;

namespace Microsoft.AspNet.SessionState
{
    internal static class AppSettings
    {
        private static volatile bool _settingsInitialized = false;
        private static object _appSettingsLock = new object();
        private static void EnsureSettingsLoaded()
        {
            if (!_settingsInitialized)
            {
                lock (_appSettingsLock)
                {
                    if (!_settingsInitialized)
                    {

                        string requestQueueLimit = null;

                        try
                        {
                            requestQueueLimit = WebConfigurationManager.AppSettings["aspnet:RequestQueueLimitPerSession"];
                        }
                        finally
                        {
                            if (requestQueueLimit == null || !int.TryParse(requestQueueLimit, out _requestQueueLimitPerSession) || _requestQueueLimitPerSession < 0)
                                _requestQueueLimitPerSession = DefaultRequestQueueLimitPerSession;

                            _settingsInitialized = true;
                        }
                    }
                }
            }
        }

        internal const int DefaultRequestQueueLimitPerSession = 50;
        // Limit of queued requests per session
        private static int _requestQueueLimitPerSession;
        internal static int RequestQueueLimitPerSession
        {
            get
            {
                EnsureSettingsLoaded();
                return _requestQueueLimitPerSession;
            }
        }
    }
}
