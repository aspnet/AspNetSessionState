// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See the License.txt file in the project root for full license information.

namespace Microsoft.AspNet.SessionState
{
    using System.Data.SqlClient;

    static class SqlCommandExtension
    {
        public static SqlParameter GetOutPutParameterValue(this SqlCommand cmd, string parameterName)
        {
            return cmd.Parameters[$"@{parameterName}"];
        }
    }
}
