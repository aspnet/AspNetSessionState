// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See the License.txt file in the project root for full license information.

namespace Microsoft.AspNet.SessionState
{
    using Microsoft.Data.SqlClient;
    using System.Data;
    using System.Threading.Tasks;
    using System.Windows.Forms;

    class SqlCommandHelper
    {
        private int _commandTimeout;

        public SqlCommandHelper(int commandTimeout)
        {
            this._commandTimeout = commandTimeout;
        }

        #region property for unit tests
        internal int CommandTimeout
        {
            get { return _commandTimeout; }
        }
        #endregion

        public SqlCommand CreateSqlCommand(string sql)
        {
            return CreateSqlCommandInternal(sql);
        }

        public SqlCommand CreateSqlCommandForSP(string spName)
        {
            return CreateSqlCommandForSPInternal(spName);
        }

        public async Task<int> CreateSProcIfDoesNotExist(string sprocName, string createSProcSql, SqlConnection connection, SqlTransaction transaction)
        {
            string cmdText = $@"SELECT Count(*) FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE = 'PROCEDURE' AND ROUTINE_NAME = '{sprocName}'";
            var cmd = new SqlCommand(cmdText, connection, transaction);
            int count = (int)await cmd.ExecuteScalarAsync();

            if (count == 0)
            {
                cmd = new SqlCommand(createSProcSql, connection, transaction);
                return await cmd.ExecuteNonQueryAsync();
            }

            return 0;
        }

        private SqlCommand CreateSqlCommandInternal(string sql)
        {
            return new SqlCommand()
            {
                CommandType = CommandType.Text,
                CommandTimeout = _commandTimeout,
                CommandText = sql
            };
        }

        private SqlCommand CreateSqlCommandForSPInternal(string spName)
        {
            return new SqlCommand()
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = _commandTimeout,
                CommandText = spName
            };
        }
    }
}
