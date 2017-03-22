// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNet.SessionState
{
    using Microsoft.AspNet.SessionState.Resources;
    using System;
    using System.Data;
    using System.Data.SqlClient;
    using System.Security.Principal;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web;

    internal enum SqlParameterName
    {
        SessionId,
        Created,
        Expires,
        LockDate,
        LockDateLocal,
        LockCookie,
        Timeout,
        Locked,
        SessionItemShort,
        SessionItemLong,
        Flags,
        LockAge,
        ActionFlags
    }

    internal static class Sec
    {
        internal const int ONE_SECOND = 1;
        internal const int ONE_MINUTE = ONE_SECOND * 60;
        internal const int ONE_HOUR = ONE_MINUTE * 60;
        internal const int ONE_DAY = ONE_HOUR * 24;
        internal const int ONE_WEEK = ONE_DAY * 7;
        internal const int ONE_YEAR = ONE_DAY * 365;
        internal const int ONE_LEAP_YEAR = ONE_DAY * 366;
    }
    internal class SqlStateExecutor : IDisposable
    {
        private const int ITEM_SHORT_LENGTH = 7000;
        private const int SQL_ERROR_PRIMARY_KEY_VIOLATION = 2627;
        private const int SQL_LOGIN_FAILED = 18456;
        private const int SQL_LOGIN_FAILED_2 = 18452;
        private const int SQL_LOGIN_FAILED_3 = 18450;
        private const int SQL_CANNOT_OPEN_DATABASE_FOR_LOGIN = 4060;
        private const int SQL_TIMEOUT_EXPIRED = -2;
        private const int APP_SUFFIX_LENGTH = 8;
        private const int FIRST_RETRY_SLEEP_TIME = 5000;
        private const int RETRY_SLEEP_TIME = 1000;
        
        private static bool s_initialized;
        private static string s_connectionString;
        private static TimeSpan s_retryInterval;
        private static bool s_sessionTableInitialized;
        private static object s_lock = new object();
                
        private SqlConnection _sqlConnection;
        private SqlCommand _sqlCmd;

        public static void Initialize(string connectionString, TimeSpan retryInterval)
        {
            if (!s_initialized)
            {
                s_connectionString = connectionString;
                s_retryInterval = retryInterval;
                s_initialized = true;
            }
        }

        public static void CreateSessionStateTableIfNeeded(ISqlStateCommandCreator cmdCreator)
        {
            if (!s_sessionTableInitialized)
            {
                lock (s_lock)
                {
                    using(var executor = new SqlStateExecutor(cmdCreator.CreateCreateSessionTableCmd()))
                    {
                        try
                        {
                            var task = executor.SqlExecuteNonQueryWithRetryAsync().ConfigureAwait(false);
                            task.GetAwaiter().GetResult();
                        }
                        catch (Exception ex)
                        {
                            ThrowSqlConnectionException(ex);
                        }
                        finally
                        {
                            s_sessionTableInitialized = true;
                        }
                    }
                }
            }
        }

        public SqlStateExecutor(SqlCommand cmd)
        {
            _sqlConnection = new SqlConnection(s_connectionString);
            _sqlCmd = cmd;
            _sqlCmd.Connection = _sqlConnection;            
        }      

        public static void ThrowSqlConnectionException(Exception e)
        {
            //TODO: relace resource string
            throw new HttpException(SR.Cant_connect_sql_session_database, e);
        }

        public async Task<int> SqlExecuteNonQueryWithRetryAsync(bool ignoreInsertPKException = false)
        {
            bool isFirstAttempt = true;
            DateTime endRetryTime = DateTime.UtcNow;

            while (true)
            {
                try
                {
                    await OpenConnectionAsync();

                    return await _sqlCmd.ExecuteNonQueryAsync();
                }
                catch (SqlException e)
                {
                    // if specified, ignore primary key violations
                    if (IsInsertPKException(e, ignoreInsertPKException))
                    {
                        // ignoreInsertPKException = insert && newItem
                        return -1;
                    }

                    if (!CanRetry(e, _sqlCmd.Connection, ref isFirstAttempt, ref endRetryTime))
                    {
                        // just throw, because not all conditions to retry are satisfied
                        ThrowSqlConnectionException(e);
                    }
                }
                catch (Exception e)
                {
                    // just throw, we have a different Exception
                    ThrowSqlConnectionException(e);
                }
            }
        }

        public async Task<SqlDataReader> SqlExecuteReaderWithRetryAsync(CommandBehavior cmdBehavior = CommandBehavior.Default)
        {
            bool isFirstAttempt = true;
            DateTime endRetryTime = DateTime.UtcNow;

            while (true)
            {
                try
                {
                    await OpenConnectionAsync();

                    return await _sqlCmd.ExecuteReaderAsync(cmdBehavior);
                }
                catch (SqlException e)
                {
                    if (!CanRetry(e, _sqlCmd.Connection, ref isFirstAttempt, ref endRetryTime))
                    {
                        // just throw, default to previous behavior
                        ThrowSqlConnectionException(e);
                    }
                }
                catch (Exception e)
                {
                    // just throw, we have a different Exception
                    ThrowSqlConnectionException(e);
                }
            }
        }

        public SqlParameter GetOutPutParameterValue(SqlParameterName parameterName)
        {
            return _sqlCmd.Parameters[$"@{parameterName}"];
        }

        public void Dispose()
        {
            if (_sqlConnection != null)
            {
                _sqlConnection.Close();
                _sqlConnection = null;
            }
        }

        private static bool IsFatalSqlException(SqlException ex)
        {
            // We will retry sql operations for serious errors.
            // We consider fatal exceptions any error with severity >= 20.
            // In this case, the SQL server closes the connection.
            // TODO: make sure other failures (cluster failoevers, etc) throw the same kind of errors
            // When Sql goes down and then is restarted, the first attempt to connect
            // sometimes results in a SqlException "Cannot open database "%.*ls" requested by the login. The login failed."
            // (number 4060, class 11)
            if (ex != null &&
                (ex.Class >= 20 ||
                 ex.Number == SQL_CANNOT_OPEN_DATABASE_FOR_LOGIN ||
                 ex.Number == SQL_TIMEOUT_EXPIRED))
            {
                return true;
            }
            return false;
        }

        private bool CanRetry(SqlException ex, SqlConnection conn,
                                            ref bool isFirstAttempt, ref DateTime endRetryTime)
        {
            if (s_retryInterval.Seconds <= 0)
            {
                // no retry policy set
                return false;
            }
            if (!IsFatalSqlException(ex))
            {
                return false;
            }
            if (isFirstAttempt)
            {
                // First time we sleep longer than for subsequent retries.
                Thread.Sleep(FIRST_RETRY_SLEEP_TIME);
                endRetryTime = DateTime.UtcNow.Add(s_retryInterval);

                isFirstAttempt = false;
                return true;
            }
            if (DateTime.UtcNow > endRetryTime)
            {
                return false;
            }
            // sleep the specified time and allow retry
            Thread.Sleep(RETRY_SLEEP_TIME);
            return true;
        }

        private void ClearConnectionAndThrow(Exception e)
        {
            SqlConnection connection = _sqlConnection;
            _sqlConnection = null;
            ThrowSqlConnectionException(e);
        }

        private async Task OpenConnectionAsync()
        {
            try
            {
                if (_sqlConnection.State != ConnectionState.Open)
                {
                    await _sqlConnection.OpenAsync();
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

                    SqlConnectionStringBuilder scsb = new SqlConnectionStringBuilder(s_connectionString);
                    if (scsb.IntegratedSecurity)
                    {
                        user = WindowsIdentity.GetCurrent().Name;
                    }
                    else
                    {
                        user = scsb.UserID;
                    }

                    // TODO: resource string
                    HttpException outerException = new HttpException(string.Format(SR.Login_failed_sql_session_database, user), e);

                    ClearConnectionAndThrow(outerException);
                }
            }
            catch (Exception e)
            {
                // just throw, we have a different Exception
                ClearConnectionAndThrow(e);
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
