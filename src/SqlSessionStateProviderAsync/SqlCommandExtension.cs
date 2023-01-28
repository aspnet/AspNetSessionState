// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See the License.txt file in the project root for full license information.

namespace Microsoft.AspNet.SessionState
{
    using System.Diagnostics;
    using Microsoft.Data.SqlClient;

    static class SqlCommandExtension
    {
        public static SqlParameter GetOutPutParameterValue(this SqlCommand cmd, string parameterName)
        {
            // This is an internal method that only we call. We know 'parameterName' always begins
            // with an '@' so we don't need to check for that case. Be aware of that expectation.
            Debug.Assert(parameterName != null);
            Debug.Assert(parameterName[0] == '@');
            return cmd.Parameters[parameterName];
        }
    }
}
