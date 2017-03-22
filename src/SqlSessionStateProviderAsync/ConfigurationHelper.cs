// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNet.SessionState
{
    using Resources;
    using System;
    using System.Configuration;
    using System.Configuration.Provider;
    using System.Globalization;

    static class ConfigurationHelper
    {
        public static ConnectionStringSettings GetConnectionString(string connectionstringName)
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
    }
}
