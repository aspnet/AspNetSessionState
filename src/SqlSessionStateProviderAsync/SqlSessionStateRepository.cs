// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See the License.txt file in the project root for full license information.

namespace Microsoft.AspNet.SessionState
{
    using Resources;
    using Microsoft.Data.SqlClient;
    using System;
    using System.Runtime.CompilerServices;
    using System.Threading.Tasks;
    using System.Web;
    using System.Web.SessionState;
    using System.Collections.Generic;
    using System.Diagnostics;

    /// <summary>
    /// SQL server version must be >= 8.0
    /// </summary>
    class SqlSessionStateRepository : ISqlSessionStateRepository
    {
        private const int DEFAULT_RETRY_INTERVAL = 1000;
        private const int DEFAULT_RETRY_NUM = 10;
        private readonly string SessionTableName = "ASPNetSessionState";

        private int _retryIntervalMilSec;
        private string _connectString;
        private int _maxRetryNum;
        private int _commandTimeout;
        private SqlCommandHelper _commandHelper;

        #region sql statement
        // SQL server version must be >= 8.0
        #region CreateSessionTable
        // SQL Type 'image' is planned for removal. Use varbinary(max) going forward. -- https://learn.microsoft.com/en-us/sql/t-sql/data-types/ntext-text-and-image-transact-sql?view=sql-server-ver16
        private readonly string CreateSessionTableSql;
        private static readonly string CreateSessionTableTemplate = @"
               IF NOT EXISTS (SELECT * 
                 FROM INFORMATION_SCHEMA.TABLES 
                 WHERE TABLE_NAME = '{0}')
               BEGIN
                CREATE TABLE {0} (
                SessionId           nvarchar(" + SqlSessionStateRepositoryUtil.IdLength + @")    NOT NULL PRIMARY KEY,
                Created             datetime        NOT NULL DEFAULT GETUTCDATE(),
                Expires             datetime        NOT NULL,
                LockDate            datetime        NOT NULL,
                LockDateLocal       datetime        NOT NULL,
                LockCookie          int             NOT NULL,
                Timeout             int             NOT NULL,
                Locked              bit             NOT NULL,
                SessionItemLong     varbinary(max)  NULL,
                Flags               int             NOT NULL DEFAULT 0,
                ) 
                CREATE NONCLUSTERED INDEX Index_Expires ON {0} (Expires)
            END";
        #endregion

        #region GetStateItemExclusive
        private readonly string GetStateItemExclusiveSP = "GetStateItemExclusive";
        private readonly string GetStateItemExclusiveSql;
        private static readonly string GetStateItemExclusiveTemplate = @"
            CREATE PROCEDURE {1} (
                    " + SqlParameterName.SessionId + @" nvarchar(" + SqlSessionStateRepositoryUtil.IdLength + @"),
                    " + SqlParameterName.Locked + @" bit OUTPUT,
                    " + SqlParameterName.LockAge + @" int OUTPUT,
                    " + SqlParameterName.LockCookie + @" int OUTPUT,
                    " + SqlParameterName.ActionFlags + @" int OUTPUT
            ) AS
                DECLARE @stateItem AS varbinary(max)
                DECLARE @now AS datetime
                DECLARE @nowLocal AS datetime
            
                SET @now = GETUTCDATE()
                SET @nowLocal = GETDATE()
            
                UPDATE {0} WITH (ROWLOCK, XLOCK)
                SET Expires = DATEADD(n, Timeout, @now), 
                    LockDate = CASE Locked
                        WHEN 0 THEN @now
                        ELSE LockDate
                        END,
                    LockDateLocal = CASE Locked
                        WHEN 0 THEN @nowLocal
                        ELSE LockDateLocal
                        END,
                    " + SqlParameterName.LockAge + @" = CASE Locked
                        WHEN 0 THEN 0
                        ELSE DATEDIFF(second, LockDate, @now)
                        END,
                    " + SqlParameterName.LockCookie + @" = LockCookie = CASE Locked
                        WHEN 0 THEN LockCookie + 1
                        ELSE LockCookie
                        END,
                    @stateItem = CASE Locked
                        WHEN 0 THEN SessionItemLong
                        ELSE NULL
                        END,
                    " + SqlParameterName.Locked + @" = Locked,
                    Locked = 1,

                    /* If the Uninitialized flag (0x1) if it is set,
                        remove it and return InitializeItem (0x1) in actionFlags */
                    Flags = CASE
                        WHEN (Flags & 1) <> 0 THEN (Flags & ~1)
                        ELSE Flags
                        END,
                    " + SqlParameterName.ActionFlags + @" = CASE
                        WHEN (Flags & 1) <> 0 THEN 1
                        ELSE 0
                        END
                WHERE SessionId = " + SqlParameterName.SessionId + @"
                IF @stateItem IS NOT NULL BEGIN
                    SELECT @stateItem
                END
            ";
        #endregion

        #region GetStateItem
        private readonly string GetStateItemSP = "GetStateItem";
        private readonly string GetStateItemSql;
        private static readonly string GetStateItemTemplate = @"
            CREATE PROCEDURE {1} (
                    " + SqlParameterName.SessionId + @" nvarchar(" + SqlSessionStateRepositoryUtil.IdLength + @"),
                    " + SqlParameterName.Locked + @" bit OUTPUT,
                    " + SqlParameterName.LockAge + @" int OUTPUT,
                    " + SqlParameterName.LockCookie + @" int OUTPUT,
                    " + SqlParameterName.ActionFlags + @" int OUTPUT
            ) AS
                DECLARE @stateItem AS varbinary(max)
                DECLARE @now AS datetime
                SET @now = GETUTCDATE()

                UPDATE {0} WITH (XLOCK, ROWLOCK)
                SET Expires = DATEADD(n, Timeout, @now), 
                    " + SqlParameterName.Locked + @" = Locked,
                    " + SqlParameterName.LockAge + @" = DATEDIFF(second, LockDate, @now),
                    " + SqlParameterName.LockCookie + @" = LockCookie,
                    @stateItem = CASE @locked
                        WHEN 0 THEN SessionItemLong
                        ELSE NULL
                        END,
                    /* If the Uninitialized flag (0x1) if it is set,
                        remove it and return InitializeItem (0x1) in actionFlags */
                    Flags = CASE
                        WHEN (Flags & 1) <> 0 THEN (Flags & ~1)
                        ELSE Flags
                        END,
                    " + SqlParameterName.ActionFlags + @" = CASE
                        WHEN (Flags & 1) <> 0 THEN 1
                        ELSE 0
                        END
                WHERE SessionId = " + SqlParameterName.SessionId + @"
                IF @stateItem IS NOT NULL BEGIN
                    SELECT @stateItem
                END
            ";
        #endregion

        #region DeleteExpiredSessions
        private readonly string DeleteExpiredSessionsSP = "DeleteExpiredSessionState";
        private readonly string DeleteExpiredSessionsSql;
        private static readonly string DeleteExpiredSessionsTemplate = @"
            CREATE PROCEDURE {1} AS
                BEGIN
                    SET NOCOUNT ON
                    SET DEADLOCK_PRIORITY LOW

                    DECLARE @now datetime
                    SET @now = GETUTCDATE() 

                    CREATE TABLE #tblExpiredSessions 
                    ( 
                        SessionId nvarchar(" + SqlSessionStateRepositoryUtil.IdLength + @") NOT NULL PRIMARY KEY
                    )

                    INSERT #tblExpiredSessions (SessionId)
                        SELECT SessionId
                        FROM {0} WITH (READUNCOMMITTED)
                        WHERE Expires < @now

                    IF @@ROWCOUNT <> 0 
                    BEGIN 
                        DECLARE ExpiredSessionCursor CURSOR LOCAL FORWARD_ONLY READ_ONLY
                        FOR SELECT SessionId FROM #tblExpiredSessions

                        DECLARE @SessionId nvarchar(" + SqlSessionStateRepositoryUtil.IdLength + @")

                        OPEN ExpiredSessionCursor

                        FETCH NEXT FROM ExpiredSessionCursor INTO @SessionId

                        WHILE @@FETCH_STATUS = 0 
                            BEGIN
                                DELETE FROM {0} WHERE SessionId = @SessionId AND Expires < @now
                                FETCH NEXT FROM ExpiredSessionCursor INTO @SessionId
                            END

                        CLOSE ExpiredSessionCursor

                        DEALLOCATE ExpiredSessionCursor
                    END 

                    DROP TABLE #tblExpiredSessions
                END
            ";
        #endregion

        #region InsertStateItem
        private readonly string InsertStateItemSP = "InsertStateItem";
        private readonly string InsertStateItemSql;
        private static readonly string InsertStateItemTemplate = @"
            CREATE PROCEDURE {1} (
                    " + SqlParameterName.SessionId + @" nvarchar(" + SqlSessionStateRepositoryUtil.IdLength + @"),
                    " + SqlParameterName.Timeout + @" int,
                    " + SqlParameterName.SessionItemLong + @" varbinary(max)
            ) AS
                DECLARE @now AS datetime
                DECLARE @nowLocal AS datetime
                SET @now = GETUTCDATE()
                SET @nowLocal = GETDATE()

                INSERT {0} 
                    (SessionId, 
                        SessionItemLong, 
                        Timeout, 
                        Expires, 
                        Locked, 
                        LockDate,
                        LockDateLocal,
                        LockCookie) 
                VALUES 
                    (" + SqlParameterName.SessionId + @", 
                        " + SqlParameterName.SessionItemLong + @", 
                        " + SqlParameterName.Timeout + @", 
                        DATEADD(n, " + SqlParameterName.Timeout + @", @now), 
                        0, 
                        @now,
                        @nowLocal,
                        1)
            ";
        #endregion

        #region InsertUninitializedItem
        private readonly string InsertUninitializedItemSP = "InsertUninitializedItem";
        private readonly string InsertUninitializedItemSql;
        private static readonly string InsertUninitializedItemTemplate = @"
            CREATE PROCEDURE {1} (
                    " + SqlParameterName.SessionId + @" nvarchar(" + SqlSessionStateRepositoryUtil.IdLength + @"),
                    " + SqlParameterName.Timeout + @" int,
                    " + SqlParameterName.SessionItemLong + @" varbinary(max)
            ) AS
                DECLARE @now AS datetime
                DECLARE @nowLocal AS datetime
                SET @now = GETUTCDATE()
                SET @nowLocal = GETDATE()

                INSERT {0} (SessionId, 
                        SessionItemLong, 
                        Timeout, 
                        Expires, 
                        Locked, 
                        LockDate,
                        LockDateLocal,
                        LockCookie,
                        Flags) 
                VALUES
                    (" + SqlParameterName.SessionId + @",
                        " + SqlParameterName.SessionItemLong + @",
                        " + SqlParameterName.Timeout + @",
                        DATEADD(n, " + SqlParameterName.Timeout + @", @now),
                        0,
                        @now,
                        @nowLocal,
                        1,
                        1)
            ";
        #endregion

        #region ReleaseItemExclusive
        private readonly string ReleaseItemExclusiveSP = "ReleaseItemExclusive";
        private readonly string ReleaseItemExclusiveSql;
        private static readonly string ReleaseItemExclusiveTemplate = @"
            CREATE PROCEDURE {1} (
                    " + SqlParameterName.SessionId + @" nvarchar(" + SqlSessionStateRepositoryUtil.IdLength + @"),
                    " + SqlParameterName.LockCookie + @" int
            ) AS
                UPDATE {0} WITH (ROWLOCK)
                SET Expires = DATEADD(n, Timeout, GETUTCDATE()),
                    Locked = 0
                WHERE SessionId = " + SqlParameterName.SessionId + @" AND LockCookie = " + SqlParameterName.LockCookie + @"
            ";
        #endregion

        #region RemoveStateItem
        private readonly string RemoveStateItemSP = "RemoveStateItem";
        private readonly string RemoveStateItemSql;
        private static readonly string RemoveStateItemTemplate = @"
            CREATE PROCEDURE {1} (
                    " + SqlParameterName.SessionId + @" nvarchar(" + SqlSessionStateRepositoryUtil.IdLength + @"),
                    " + SqlParameterName.LockCookie + @" int
            ) AS
                DELETE {0}
                WHERE SessionId = " + SqlParameterName.SessionId + @" AND LockCookie = " + SqlParameterName.LockCookie + @"
            ";
        #endregion

        #region ResetItemTimeout
        private readonly string ResetItemTimeoutSP = "ResetItemTimeout";
        private readonly string ResetItemTimeoutSql;
        private static readonly string ResetItemTimeoutTemplate = @"
            CREATE PROCEDURE {1} (
                    " + SqlParameterName.SessionId + @" nvarchar(" + SqlSessionStateRepositoryUtil.IdLength + @")
            ) AS
                UPDATE {0}
                SET Expires = DATEADD(n, Timeout, GETUTCDATE())
                WHERE SessionId = " + SqlParameterName.SessionId + @"
            ";
        #endregion

        #region UpdateStateItem
        private readonly string UpdateStateItemSP = "UpdateStateItem";
        private readonly string UpdateStateItemSql;
        private static readonly string UpdateStateItemTemplate = @"
            CREATE PROCEDURE {1} (
                    " + SqlParameterName.SessionId + @" nvarchar(" + SqlSessionStateRepositoryUtil.IdLength + @"),
                    " + SqlParameterName.LockCookie + @" int,
                    " + SqlParameterName.Timeout + @" int,
                    " + SqlParameterName.SessionItemLong + @" varbinary(max)
            ) AS
                UPDATE {0} WITH (ROWLOCK)
                SET Expires = DATEADD(n, " + SqlParameterName.Timeout + @", GETUTCDATE()), 
                    SessionItemLong = " + SqlParameterName.SessionItemLong + @",
                    Timeout = " + SqlParameterName.Timeout + @",
                    Locked = 0
                WHERE SessionId = " + SqlParameterName.SessionId + @" AND LockCookie = " + SqlParameterName.LockCookie + @"
            ";
        #endregion

        #endregion

        public SqlSessionStateRepository(string connectionString, string sessionTableName, int commandTimeout,
            int? retryInterval = DEFAULT_RETRY_INTERVAL, int? retryNum = DEFAULT_RETRY_NUM)
        {
            this._retryIntervalMilSec = retryInterval.HasValue ? retryInterval.Value : DEFAULT_RETRY_INTERVAL;
            this._connectString = connectionString;
            this._maxRetryNum = retryNum.HasValue ? retryNum.Value : DEFAULT_RETRY_NUM;
            this._commandTimeout = commandTimeout;
            this._commandHelper = new SqlCommandHelper(commandTimeout);

            if (!String.IsNullOrWhiteSpace(sessionTableName))
            {
                SessionTableName = sessionTableName;

                // When using a non-default table name, prefix the SProcs to avoid confusion/collision.
                GetStateItemExclusiveSP = SessionTableName + "_" + GetStateItemExclusiveSP;
                GetStateItemSP = SessionTableName + "_" + GetStateItemSP;
                DeleteExpiredSessionsSP = SessionTableName + "_" + DeleteExpiredSessionsSP;
                InsertUninitializedItemSP = SessionTableName + "_" + InsertUninitializedItemSP;
                ReleaseItemExclusiveSP = SessionTableName + "_" + ReleaseItemExclusiveSP;
                RemoveStateItemSP = SessionTableName + "_" + RemoveStateItemSP;
                ResetItemTimeoutSP = SessionTableName + "_" + ResetItemTimeoutSP;
                UpdateStateItemSP = SessionTableName + "_" + UpdateStateItemSP;
                InsertStateItemSP = SessionTableName + "_" + InsertStateItemSP;
            }

            // Create SQL commands from templates once. (This constructor is 1-time-init enforced by SqlSessionStateProviderAsync.Initialize)
            CreateSessionTableSql = String.Format(CreateSessionTableTemplate, SessionTableName);
            GetStateItemExclusiveSql = String.Format(GetStateItemExclusiveTemplate, SessionTableName, GetStateItemExclusiveSP);
            GetStateItemSql = String.Format(GetStateItemTemplate, SessionTableName, GetStateItemSP);
            DeleteExpiredSessionsSql = String.Format(DeleteExpiredSessionsTemplate, SessionTableName, DeleteExpiredSessionsSP);
            InsertStateItemSql = String.Format(InsertStateItemTemplate, SessionTableName, InsertStateItemSP);
            InsertUninitializedItemSql = String.Format(InsertUninitializedItemTemplate, SessionTableName, InsertUninitializedItemSP);
            ReleaseItemExclusiveSql = String.Format(ReleaseItemExclusiveTemplate, SessionTableName, ReleaseItemExclusiveSP);
            RemoveStateItemSql = String.Format(RemoveStateItemTemplate, SessionTableName, RemoveStateItemSP);
            ResetItemTimeoutSql = String.Format(ResetItemTimeoutTemplate, SessionTableName, ResetItemTimeoutSP);
            UpdateStateItemSql = String.Format(UpdateStateItemTemplate, SessionTableName, UpdateStateItemSP);
        }

        #region properties/methods for unit tests
        internal int RetryIntervalMilSec
        {
            get { return _retryIntervalMilSec; }
        }

        internal string ConnectString
        {
            get { return _connectString; }
        }

        internal int MaxRetryNum
        {
            get { return _maxRetryNum; }
        }

        internal int CommandTimeout
        {
            get { return _commandTimeout; }
        }

        internal string SessionStateTableName
        {
            get { return SessionTableName; }
        }
        #endregion

        #region ISqlSessionStateRepository implementation
        public void CreateSessionStateTable()
        {
            // This is going to be a lot nicer with 'await'
            var task = CreateSessionStateTableAsync().ConfigureAwait(false);
            task.GetAwaiter().GetResult();
        }

        private async Task<bool> CreateSessionStateTableAsync()
        {
            SqlTransaction transaction;

            using (var connection = new SqlConnection(_connectString))
            {
                await connection.OpenAsync();
                transaction = connection.BeginTransaction();

                try
                {

                    // Ensure the State table is created
                    var cmd = new SqlCommand(CreateSessionTableSql, connection, transaction);
                    await cmd.ExecuteNonQueryAsync();

                    // Ensure necessary SProcs exist as well
                    await _commandHelper.CreateSProcIfDoesNotExist(GetStateItemExclusiveSP, GetStateItemExclusiveSql, connection, transaction);
                    await _commandHelper.CreateSProcIfDoesNotExist(GetStateItemSP, GetStateItemSql, connection, transaction);
                    await _commandHelper.CreateSProcIfDoesNotExist(DeleteExpiredSessionsSP, DeleteExpiredSessionsSql, connection, transaction);
                    await _commandHelper.CreateSProcIfDoesNotExist(InsertUninitializedItemSP, InsertUninitializedItemSql, connection, transaction);
                    await _commandHelper.CreateSProcIfDoesNotExist(ReleaseItemExclusiveSP, ReleaseItemExclusiveSql, connection, transaction);
                    await _commandHelper.CreateSProcIfDoesNotExist(RemoveStateItemSP, RemoveStateItemSql, connection, transaction);
                    await _commandHelper.CreateSProcIfDoesNotExist(ResetItemTimeoutSP, ResetItemTimeoutSql, connection, transaction);
                    await _commandHelper.CreateSProcIfDoesNotExist(UpdateStateItemSP, UpdateStateItemSql, connection, transaction);
                    await _commandHelper.CreateSProcIfDoesNotExist(InsertStateItemSP, InsertStateItemSql, connection, transaction);

                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    try { transaction.Rollback(); } catch (Exception) { }
                    throw new HttpException(SR.Cant_connect_sql_session_database, ex);
                }
            }

            return true;
        }

        public void DeleteExpiredSessions()
        {
            using (var connection = new SqlConnection(_connectString))
            {
                var cmd = _commandHelper.CreateSqlCommandForSP(DeleteExpiredSessionsSP);
                var task = SqlSessionStateRepositoryUtil.SqlExecuteNonQueryWithRetryAsync(connection, cmd, CanRetryAsync).ConfigureAwait(false);
                task.GetAwaiter().GetResult();
            }
        }

        public async Task<SessionItem> GetSessionStateItemAsync(string id, bool exclusive)
        {
            var locked = false;
            var lockAge = TimeSpan.Zero;
            var now = DateTime.UtcNow;
            object lockId = null;
            byte[] buf = null;
            SessionStateActions actions = SessionStateActions.None;
            SqlCommand cmd = null;

            if (exclusive)
            {
                cmd = _commandHelper.CreateSqlCommandForSP(GetStateItemExclusiveSP);
                cmd.Parameters.AddSessionIdParameter(id)
                              .AddLockedParameter()
                              .AddLockAgeParameter()
                              .AddLockCookieParameter()
                              .AddActionFlagsParameter();
            }
            else
            {
                cmd = _commandHelper.CreateSqlCommandForSP(GetStateItemSP);
                cmd.Parameters.AddSessionIdParameter(id)
                              .AddLockAgeParameter()
                              .AddLockedParameter()
                              .AddLockCookieParameter()
                              .AddActionFlagsParameter();
            }

            using (var connection = new SqlConnection(_connectString))
            {
                using (var reader = await SqlSessionStateRepositoryUtil.SqlExecuteReaderWithRetryAsync(connection, cmd, CanRetryAsync))
                {
                    if (await reader.ReadAsync())
                    {
                        // Varbinary(max) should not be returned in an output parameter
                        // Read the returned dataset consisting of SessionItemLong if found
                        buf = await reader.GetFieldValueAsync<byte[]>(0);
                    }
                }

                var outParameterLocked = cmd.GetOutPutParameterValue(SqlParameterName.Locked);
                if (outParameterLocked == null || Convert.IsDBNull(outParameterLocked.Value))
                {
                    return null;
                }
                locked = (bool)outParameterLocked.Value;
                lockId = (int)cmd.GetOutPutParameterValue(SqlParameterName.LockCookie).Value;

                if (locked)
                {
                    lockAge = new TimeSpan(0, 0, (int)cmd.GetOutPutParameterValue(SqlParameterName.LockAge).Value);

                    if (lockAge > new TimeSpan(0, 0, Sec.ONE_YEAR))
                    {
                        lockAge = TimeSpan.Zero;
                    }
                    return new SessionItem(null, true, lockAge, lockId, actions);
                }
                actions = (SessionStateActions)cmd.GetOutPutParameterValue(SqlParameterName.ActionFlags).Value;

                Debug.Assert(buf != null);

                return new SessionItem(buf, true, lockAge, lockId, actions);
            }
        }

        public async Task CreateOrUpdateSessionStateItemAsync(bool newItem, string id, byte[] buf, int length, int timeout, int lockCookie, int orginalStreamLen)
        {
            SqlCommand cmd;

            if (!newItem)
            {
                cmd = _commandHelper.CreateSqlCommandForSP(UpdateStateItemSP);
                cmd.Parameters.AddSessionIdParameter(id)
                              .AddLockCookieParameter(lockCookie)
                              .AddTimeoutParameter(timeout)
                              .AddSessionItemLongVarBinaryParameter(length, buf);
            }
            else
            {
                cmd = _commandHelper.CreateSqlCommandForSP(InsertStateItemSP);
                cmd.Parameters.AddSessionIdParameter(id)
                              .AddTimeoutParameter(timeout)
                              .AddSessionItemLongVarBinaryParameter(length, buf);
            }

            using (var connection = new SqlConnection(_connectString))
            {
                await SqlSessionStateRepositoryUtil.SqlExecuteNonQueryWithRetryAsync(connection, cmd, CanRetryAsync, newItem);
            }
        }

        public async Task ResetSessionItemTimeoutAsync(string id)
        {
            var cmd = _commandHelper.CreateSqlCommandForSP(ResetItemTimeoutSP);
            cmd.Parameters.AddSessionIdParameter(id);
            using (var connection = new SqlConnection(_connectString))
            {
                await SqlSessionStateRepositoryUtil.SqlExecuteNonQueryWithRetryAsync(connection, cmd, CanRetryAsync);
            }
        }

        public async Task RemoveSessionItemAsync(string id, object lockId)
        {
            var cmd = _commandHelper.CreateSqlCommandForSP(RemoveStateItemSP);
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddLockCookieParameter(lockId);
            using (var connection = new SqlConnection(_connectString))
            {
                await SqlSessionStateRepositoryUtil.SqlExecuteNonQueryWithRetryAsync(connection, cmd, CanRetryAsync);
            }
        }

        public async Task ReleaseSessionItemAsync(string id, object lockId)
        {
            var cmd = _commandHelper.CreateSqlCommandForSP(ReleaseItemExclusiveSP);
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddLockCookieParameter(lockId);
            using (var connection = new SqlConnection(_connectString))
            {
                await SqlSessionStateRepositoryUtil.SqlExecuteNonQueryWithRetryAsync(connection, cmd, CanRetryAsync);
            }
        }

        public async Task CreateUninitializedSessionItemAsync(string id, int length, byte[] buf, int timeout)
        {
            var cmd = _commandHelper.CreateSqlCommandForSP(InsertUninitializedItemSP);
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddTimeoutParameter(timeout)
                          .AddSessionItemLongVarBinaryParameter(length, buf);
            using (var connection = new SqlConnection(_connectString))
            {
                await SqlSessionStateRepositoryUtil.SqlExecuteNonQueryWithRetryAsync(connection, cmd, CanRetryAsync, true);
            }
        }
        #endregion

        private Task<bool> CanRetryAsync(RetryCheckParameter parameter)
        {
            if (_retryIntervalMilSec <= 0 ||
                !SqlSessionStateRepositoryUtil.IsFatalSqlException(parameter.Exception) ||
                parameter.RetryCount >= _maxRetryNum)
            {
                return Task.FromResult(false);
            }

            return WaitToRetryAsync(parameter, _retryIntervalMilSec);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task<bool> WaitToRetryAsync(RetryCheckParameter parameter, int retryIntervalMilSec)
        {
            // sleep the specified time and allow retry
            await Task.Delay(retryIntervalMilSec);
            parameter.RetryCount++;

            return true;
        }
    }
}