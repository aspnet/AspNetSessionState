namespace Microsoft.AspNet.SessionState
{
    using System.Data;
    using System.Data.SqlClient;

    class SqlCommandHelper
    {
        private int _commandTimeout;

        public SqlCommandHelper(int commandTimeout)
        {
            this._commandTimeout = commandTimeout;
        }

        public SqlCommand CreateNewSessionTableCmd(string createSessionTableSql)
        {
            return CreateSqlCommand(createSessionTableSql);
        }

        public SqlCommand CreateGetStateItemExclusiveCmd(string getStateItemExclusiveSql, string id)
        {
            var cmd = CreateSqlCommand(getStateItemExclusiveSql);
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemShortParameter()
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
                          .AddSessionItemShortParameter()
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
                          .AddSessionItemShortParameter(length, buf)
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

        public SqlCommand CreateUpdateStateItemShortCmd(string updateStateItemShortSql, 
            string id, byte[] buf, int length, int timeout, int lockCookie)
        {
            var cmd = CreateSqlCommand(updateStateItemShortSql);
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemShortParameter(length, buf)
                          .AddTimeoutParameter(timeout)
                          .AddLockCookieParameter(lockCookie);

            return cmd;
        }

        public SqlCommand CreateUpdateStateItemShortNullLongCmd(string updateStateItemShortNullLongSql, 
            string id, byte[] buf, int length, int timeout, int lockCookie)
        {
            var cmd = CreateSqlCommand(updateStateItemShortNullLongSql);
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemShortParameter(length, buf)
                          .AddTimeoutParameter(timeout)
                          .AddLockCookieParameter(lockCookie);

            return cmd;
        }

        public SqlCommand CreateUpdateStateItemLongNullShortCmd(string updateStateItemLongNullShortSql, 
            string id, byte[] buf, int length, int timeout, int lockCookie)
        {
            var cmd = CreateSqlCommand(updateStateItemLongNullShortSql);
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemLongParameter(length, buf)
                          .AddTimeoutParameter(timeout)
                          .AddLockCookieParameter(lockCookie);

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

        public SqlCommand CreateInsertStateItemShortCmd(string insertStateItemShortSql, 
            string id, byte[] buf, int length, int timeout)
        {
            var cmd = CreateSqlCommand(insertStateItemShortSql);
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemShortParameter(length, buf)
                          .AddTimeoutParameter(timeout);

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
