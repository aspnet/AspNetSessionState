// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNet.SessionState
{
    using System.Data.SqlClient;

    static class SqlCommandExtension
    {
        public static SqlParameter GetOutPutParameterValue(this SqlCommand cmd, SqlParameterName parameterName)
        {
            return cmd.Parameters[$"@{parameterName}"];
        }
    }
}
