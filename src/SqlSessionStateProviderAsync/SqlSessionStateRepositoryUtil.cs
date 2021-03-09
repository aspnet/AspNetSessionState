﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See the License.txt file in the project root for full license information.

namespace Microsoft.AspNet.SessionState
{
    using Resources;
    using System;
    using System.Data;
    using System.Data.SqlClient;
using System.Runtime.CompilerServices;
    using System.Security.Principal;
    using System.Threading.Tasks;
    using System.Web;

    enum SqlParameterName
    {
        SessionId,
        Created,
        Expires,
        LockDate,
        LockDateLocal,
        LockCookie,
        Timeout,
        Locked,
        SessionItemLong,
        Flags,
        LockAge,
        ActionFlags
    }

    static class Sec
    {
        internal const int ONE_SECOND = 1;
        internal const int ONE_MINUTE = ONE_SECOND * 60;
        internal const int ONE_HOUR = ONE_MINUTE * 60;
        internal const int ONE_DAY = ONE_HOUR * 24;
        internal const int ONE_WEEK = ONE_DAY * 7;
        internal const int ONE_YEAR = ONE_DAY * 365;
        internal const int ONE_LEAP_YEAR = ONE_DAY * 366;
    }

    class RetryCheckParameter
    {
        public SqlException Exception { get; set; }
        public DateTime EndRetryTime { get; set; }
        public int RetryCount { get; set; }
    }

    class SqlSessionStateRepositoryUtil
    {
        private const int ITEM_SHORT_LENGTH = 7000;
        private const int SQL_ERROR_PRIMARY_KEY_VIOLATION = 2627;
        private const int SQL_LOGIN_FAILED = 18456;
        private const int SQL_LOGIN_FAILED_2 = 18452;
        private const int SQL_LOGIN_FAILED_3 = 18450;
        private const int SQL_CANNOT_OPEN_DATABASE_FOR_LOGIN = 4060;
        private const int SQL_TIMEOUT_EXPIRED = -2;
        private const int APP_SUFFIX_LENGTH = 8;

        public static readonly string TableName = "ASPStateTempSessions";
        public static readonly int IdLength = 88;
        public static readonly int DefaultItemLength = 7000;

        public static async Task<int> SqlExecuteNonQueryWithRetryAsync(SqlConnection connection, SqlCommand sqlCmd, 
            Func<RetryCheckParameter, Task<bool>> canRetry, bool ignoreInsertPKException = false)
        {
            var retryParamenter = new RetryCheckParameter() { EndRetryTime = DateTime.UtcNow, RetryCount = 0 };
            sqlCmd.Connection = connection;

            while (true)
            {
                try
                {
                    await OpenConnectionAsync(connection);

                    return await sqlCmd.ExecuteNonQueryAsync();
                }
                catch (SqlException e)
                {
                    // if specified, ignore primary key violations
                    if (IsInsertPKException(e, ignoreInsertPKException))
                    {
                        // ignoreInsertPKException = insert && newItem
                        return -1;
                    }

                    retryParamenter.Exception = e;
                    if (!(await canRetry(retryParamenter)))
                    {
                        throw;
                    }
                }
            }
        }

        public static async Task<SqlDataReader> SqlExecuteReaderWithRetryAsync(SqlConnection connection, SqlCommand sqlCmd, Func<RetryCheckParameter, Task<bool>> canRetry, 
            CommandBehavior cmdBehavior = CommandBehavior.Default)
        {
            var retryParamenter = new RetryCheckParameter() { EndRetryTime = DateTime.UtcNow, RetryCount = 0 };
            sqlCmd.Connection = connection;

            while (true)
            {
                try
                {
                    await OpenConnectionAsync(connection);

                    return await sqlCmd.ExecuteReaderAsync(cmdBehavior);
                }
                catch (SqlException e)
                {
                    retryParamenter.Exception = e;
                    if (!(await canRetry(retryParamenter)))
                    {
                        throw;
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFatalSqlException(SqlException ex)
        {
            // We will retry sql operations for serious errors.
            // We consider fatal exceptions any error with severity >= 20.
            // In this case, the SQL server closes the connection.
            if (ex != null &&
                (ex.Class >= 20 ||
                 ex.Number == SQL_CANNOT_OPEN_DATABASE_FOR_LOGIN ||
                 ex.Number == SQL_TIMEOUT_EXPIRED))
            {
                return true;
            }
            return false;
        }

        private static async Task OpenConnectionAsync(SqlConnection sqlConnection)
        {
            try
            {
                if (sqlConnection.State != ConnectionState.Open)
                {
                    await sqlConnection.OpenAsync();
                }
            }
            catch (SqlException e)
            {
                if (e != null &&
                    (e.Number == SQL_LOGIN_FAILED ||
                     e.Number == SQL_LOGIN_FAILED_2 ||
                     e.Number == SQL_LOGIN_FAILED_3))
                {
                    string user;

                    SqlConnectionStringBuilder scsb = new SqlConnectionStringBuilder(sqlConnection.ConnectionString);
                    if (scsb.IntegratedSecurity)
                    {
                        user = WindowsIdentity.GetCurrent().Name;
                    }
                    else
                    {
                        user = scsb.UserID;
                    }

                    throw new HttpException(string.Format(SR.Login_failed_sql_session_database, user), e);
                }
            }
            catch (Exception e)
            {
                // just throw, we have a different Exception
                throw new HttpException(SR.Cant_connect_sql_session_database, e);
            }
        }

        private static bool IsInsertPKException(SqlException ex, bool ignoreInsertPKException)
        {
            // If the severity is greater than 20, we have a serious error.
            // The server usually closes the connection in these cases.
            if (ex != null &&
                 ex.Number == SQL_ERROR_PRIMARY_KEY_VIOLATION &&
                 ignoreInsertPKException)
            {
                // It's possible that two threads (from the same session) are creating the session
                // state, both failed to get it first, and now both tried to insert it.
                // One thread may lose with a Primary Key Violation error. If so, that thread will
                // just lose and exit gracefully.
                return true;
            }
            return false;
        }
    }
}
