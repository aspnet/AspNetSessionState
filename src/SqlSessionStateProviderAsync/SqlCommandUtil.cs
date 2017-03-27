// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNet.SessionState
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Data.SqlClient;
    using System.Diagnostics;

    
    internal class SqlCommandUtil
    {
        public static readonly string TableName = "ASPStateTempSessions";
        public static readonly int IdLength = 88;
        public static readonly int ItemShortLength = 7000;
    }
}
