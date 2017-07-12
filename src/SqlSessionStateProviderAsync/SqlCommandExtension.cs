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
