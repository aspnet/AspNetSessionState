// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See the License.txt file in the project root for full license information.

namespace Microsoft.AspNet.SessionState
{
    using Microsoft.Data.SqlClient;
    using System.Data;

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

        public SqlCommand CreateNewSessionTableCmd(string createSessionTableSql)
        {
            return CreateSqlCommand(createSessionTableSql);
        }

        public SqlCommand CreateGetStateItemExclusiveCmd(string getStateItemExclusiveSql, string id)
        {
            var cmd = CreateSqlCommand(getStateItemExclusiveSql);
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddLockAgeParameter()
                          .AddLockedParameter()
                          .AddLockCookieParameter()
                          .AddActionFlagsParameter();

            return cmd;
        }

        public SqlCommand CreateGetStateItemCmd(string getStateItemSql, string id)
        {
            var cmd = CreateSqlCommand(getStateItemSql);
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddLockedParameter()
                          .AddLockAgeParameter()
                          .AddLockCookieParameter()
                          .AddActionFlagsParameter();

            return cmd;
        }

        public SqlCommand CreateDeleteExpiredSessionsCmd(string deleteExpiredSessionsSql)
        {
            return CreateSqlCommand(deleteExpiredSessionsSql);
        }

        public SqlCommand CreateTempInsertUninitializedItemCmd(string tempInsertUninitializedItemSql, 
            string id, int length, byte[] buf, int timeout)
        {
            var cmd = CreateSqlCommand(tempInsertUninitializedItemSql);
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemLongParameter(length, buf)
                          .AddTimeoutParameter(timeout);

            return cmd;
        }

        public SqlCommand CreateReleaseItemExclusiveCmd(string releaseItemExclusiveSql, string id, object lockid)
        {
            var cmd = CreateSqlCommand(releaseItemExclusiveSql);
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddLockCookieParameter(lockid);

            return cmd;
        }

        public SqlCommand CreateRemoveStateItemCmd(string removeStateItemSql, string id, object lockid)
        {
            var cmd = CreateSqlCommand(removeStateItemSql);
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddLockCookieParameter(lockid);

            return cmd;
        }

        public SqlCommand CreateResetItemTimeoutCmd(string resetItemTimeoutSql, string id)
        {
            var cmd = CreateSqlCommand(resetItemTimeoutSql);
            cmd.Parameters.AddSessionIdParameter(id);

            return cmd;
        }

        public SqlCommand CreateUpdateStateItemLongCmd(string updateStateItemLongSql, 
            string id, byte[] buf, int length, int timeout, int lockCookie)
        {
            var cmd = CreateSqlCommand(updateStateItemLongSql);
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemLongParameter(length, buf)
                          .AddTimeoutParameter(timeout)
                          .AddLockCookieParameter(lockCookie);

            return cmd;
        }
        
        public SqlCommand CreateInsertStateItemLongCmd(string insertStateItemLongSql, 
            string id, byte[] buf, int length, int timeout)
        {
            var cmd = CreateSqlCommand(insertStateItemLongSql);
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemLongParameter(length, buf)
                          .AddTimeoutParameter(timeout);

            return cmd;
        }

        private SqlCommand CreateSqlCommand(string sql)
        {
            return new SqlCommand()
            {
                CommandType = CommandType.Text,
                CommandTimeout = _commandTimeout,
                CommandText = sql
            };
        }

    }
}
