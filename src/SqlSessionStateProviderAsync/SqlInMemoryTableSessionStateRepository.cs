// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See the License.txt file in the project root for full license information.

namespace Microsoft.AspNet.SessionState
{
    using Resources;
    using System;
    using System.Data.SqlClient;
    using System.Diagnostics;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web;
    using System.Web.SessionState;

    class SqlInMemoryTableSessionStateRepository : ISqlSessionStateRepository
    {
        private const int DEFAULT_RETRY_NUM = 10;
        private const int DEFAULT_RETRY_INERVAL = 1;

        private int _retryIntervalMilSec;
        private string _connectString;
        private int _maxRetryNum;
        private int _commandTimeout;
        private SqlCommandHelper _commandHelper;

        #region Sql statement
        // Most of the SQL statements should just work, the following statements are different
        #region CreateSessionTable
        private const string CreateSessionTableSql = @"
               IF NOT EXISTS (SELECT * 
                 FROM INFORMATION_SCHEMA.TABLES 
                 WHERE TABLE_NAME = '" + SqlSessionStateRepositoryUtil.TableName + @"')
               BEGIN
                CREATE TABLE " + SqlSessionStateRepositoryUtil.TableName + @" (
                SessionId           nvarchar(88)    COLLATE Latin1_General_100_BIN2 NOT NULL,
                Created             datetime        NOT NULL DEFAULT GETUTCDATE(),
                Expires             datetime        NOT NULL,
                LockDate            datetime        NOT NULL,
                LockDateLocal       datetime        NOT NULL,
                LockCookie          int             NOT NULL,
                Timeout             int             NOT NULL,
                Locked              bit             NOT NULL,
                SessionItemLong     varbinary(max)           NULL,
                Flags               int             NOT NULL DEFAULT 0,
                INDEX [Index_Expires] NONCLUSTERED 
                (
	                [Expires] ASC
                ),
                PRIMARY KEY NONCLUSTERED HASH 
                (
	                [SessionId]
                )WITH ( BUCKET_COUNT = 33554432)
                )WITH ( MEMORY_OPTIMIZED = ON , DURABILITY = SCHEMA_ONLY )                
              END";
        #endregion

        #region GetStateItemExclusive            
        private const string GetStateItemExclusiveSql = @"
                    DECLARE @textptr AS varbinary(max)
                    DECLARE @length AS int
                    DECLARE @now AS datetime
                    DECLARE @nowLocal AS datetime

                    SET @now = GETUTCDATE()
                    SET @nowLocal = GETDATE()

                    DECLARE @LockedCheck bit
                    DECLARE @Flags int

                    SELECT @LockedCheck = Locked, @Flags = Flags 
                        FROM " + SqlSessionStateRepositoryUtil.TableName + @"
                        WHERE SessionID = " + SqlParameterName.SessionId + @"
                    IF @Flags&1 <> 0
                    BEGIN
                        SET " + SqlParameterName.ActionFlags + @" = 1
                        UPDATE " + SqlSessionStateRepositoryUtil.TableName + @"
                            SET Flags = Flags & ~1 WHERE SessionID = " + SqlParameterName.SessionId + @"
                    END
                    ELSE
                        SET " + SqlParameterName.ActionFlags + @" = 0

                    IF @LockedCheck = 1
                    BEGIN
                        UPDATE " + SqlSessionStateRepositoryUtil.TableName + @"
                        SET Expires = DATEADD(n, Timeout, @now), 
                            " + SqlParameterName.LockAge + @" = DATEDIFF(second, LockDate, @now),
                            " + SqlParameterName.LockCookie + @" = LockCookie,
                            --@textptr = NULL,
                            @length = NULL,
                            " + SqlParameterName.Locked + @" = 1
                        WHERE SessionId = " + SqlParameterName.SessionId + @"
                    END
                    ELSE
                    BEGIN
                        UPDATE " + SqlSessionStateRepositoryUtil.TableName + @"
                        SET Expires = DATEADD(n, Timeout, @now), 
                            LockDate = @now,
                            LockDateLocal = @nowlocal,
                            " + SqlParameterName.LockAge + @" = 0,
                            " + SqlParameterName.LockCookie + @" = LockCookie = LockCookie + 1,
                            @textptr = SessionItemLong,
                            @length = 1,
                            " + SqlParameterName.Locked + @" = 0,
                            Locked = 1
                        WHERE SessionId = " + SqlParameterName.SessionId + @"

                        IF @TextPtr IS NOT NULL
                            SELECT @TextPtr
                    END
                ";
        #endregion

        #region GetStateItem            
        private const string GetStateItemSql = @"                
                    DECLARE @textptr AS varbinary(max)
                    DECLARE @length AS int
                    DECLARE @now AS datetime
                    SET @now = GETUTCDATE()

                    UPDATE " + SqlSessionStateRepositoryUtil.TableName + @"
                    SET Expires = DATEADD(n, Timeout, @now), 
                        " + SqlParameterName.Locked + @" = Locked,
                        " + SqlParameterName.LockAge + @" = DATEDIFF(second, LockDate, @now),
                        " + SqlParameterName.LockCookie + @" = LockCookie,                   
                        @textptr = CASE " + SqlParameterName.Locked + @"
                            WHEN 0 THEN SessionItemLong
                            ELSE NULL
                            END,
                        @length = CASE " + SqlParameterName.Locked + @"
                            WHEN 0 THEN DATALENGTH(SessionItemLong)
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
                    IF @length IS NOT NULL BEGIN
                        SELECT @textptr
                    END
            ";
        #endregion

        #region DeleteExpiredSessions
        private const string DeleteExpiredSessionsSql = @"
                SET NOCOUNT ON
                SET DEADLOCK_PRIORITY LOW 

                DECLARE @now datetime
                SET @now = GETUTCDATE() 

                CREATE TABLE #tblExpiredSessions 
                ( 
                    SessionId nvarchar({SqlSessionStateRepositoryUtil.IdLength}) NOT NULL PRIMARY KEY
                )

                INSERT #tblExpiredSessions (SessionId)
                    SELECT SessionId
                    FROM " + SqlSessionStateRepositoryUtil.TableName + @" WITH (SNAPSHOT)
                    WHERE Expires < @now

                IF @@ROWCOUNT <> 0 
                BEGIN 
                    DECLARE ExpiredSessionCursor CURSOR LOCAL FORWARD_ONLY READ_ONLY
                    FOR SELECT SessionId FROM #tblExpiredSessions 

                    DECLARE @SessionId nvarchar({SqlSessionStateRepositoryUtil.IdLength})

                    OPEN ExpiredSessionCursor

                    FETCH NEXT FROM ExpiredSessionCursor INTO @SessionId

                    WHILE @@FETCH_STATUS = 0 
                        BEGIN
                            DELETE FROM " + SqlSessionStateRepositoryUtil.TableName + @" WHERE SessionId = @SessionId AND Expires < @now
                            FETCH NEXT FROM ExpiredSessionCursor INTO @SessionId
                        END

                    CLOSE ExpiredSessionCursor

                    DEALLOCATE ExpiredSessionCursor

                END 

                DROP TABLE #tblExpiredSessions";
        #endregion

        #region TempInsertUninitializedItem
        private const string TempInsertUninitializedItemSql = @"
            DECLARE @now AS datetime
            DECLARE @nowLocal AS datetime
            SET @now = GETUTCDATE()
            SET @nowLocal = GETDATE()

            INSERT " + SqlSessionStateRepositoryUtil.TableName + @" (SessionId, 
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
                 1)";
        #endregion

        #region ReleaseItemExclusive
        private const string ReleaseItemExclusiveSql = @"
            UPDATE " + SqlSessionStateRepositoryUtil.TableName + @"
            SET Expires = DATEADD(n, Timeout, GETUTCDATE()),
                Locked = 0
            WHERE SessionId = " + SqlParameterName.SessionId + @" AND LockCookie = " + SqlParameterName.LockCookie;
        #endregion

        #region RemoveStateItem
        private const string RemoveStateItemSql = @"
            DELETE " + SqlSessionStateRepositoryUtil.TableName + @"
            WHERE SessionId = " + SqlParameterName.SessionId + @" AND LockCookie = " + SqlParameterName.LockCookie;
        #endregion

        #region ResetItemTimeout
        private const string ResetItemTimeoutSql = @"
            UPDATE " + SqlSessionStateRepositoryUtil.TableName + @"
            SET Expires = DATEADD(n, Timeout, GETUTCDATE())
            WHERE SessionId = " + SqlParameterName.SessionId;
        #endregion
        
        #region UpdateStateItemLong
        private const string UpdateStateItemLongSql = @"
            UPDATE " + SqlSessionStateRepositoryUtil.TableName + @"
            SET Expires = DATEADD(n, " + SqlParameterName.Timeout + @", GETUTCDATE()), 
                SessionItemLong = " + SqlParameterName.SessionItemLong + @",
                Timeout = " + SqlParameterName.Timeout + @",
                Locked = 0
            WHERE SessionId = " + SqlParameterName.SessionId + @" AND LockCookie = " + SqlParameterName.LockCookie;
        #endregion

        #region InsertStateItemLong
        private const string InsertStateItemLongSql = @"
            DECLARE @now AS datetime
            DECLARE @nowLocal AS datetime
            
            SET @now = GETUTCDATE()
            SET @nowLocal = GETDATE()

            INSERT " + SqlSessionStateRepositoryUtil.TableName + @" 
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
                 1)";
        #endregion
        #endregion

        public SqlInMemoryTableSessionStateRepository(string connectionString, int commandTimeout, 
            int? retryInterval, int? retryNum)
        {
            this._retryIntervalMilSec = retryInterval.HasValue ? retryInterval.Value : DEFAULT_RETRY_INERVAL;
            this._connectString = connectionString;
            this._maxRetryNum = retryNum.HasValue ? retryNum.Value : DEFAULT_RETRY_NUM;
            this._commandTimeout = commandTimeout;
            this._commandHelper = new SqlCommandHelper(commandTimeout);
        }

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

        public void CreateSessionStateTable()
        {
            using (var connection = new SqlConnection(_connectString))
            {
                try
                {
                    var cmd = _commandHelper.CreateNewSessionTableCmd(CreateSessionTableSql);
                    var task = SqlSessionStateRepositoryUtil.SqlExecuteNonQueryWithRetryAsync(connection, cmd, CanRetryAsync).ConfigureAwait(false);
                    task.GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    // Indicate that the DB doesn't support InMemoryTable
                    var sqlException = ex as SqlException;
                    if (sqlException != null && 
                        // 40536 error code from SQL Azure
                        // 41337 error code from SQL server
                        (sqlException.Number == 40536 || sqlException.Number == 41337))
                    {
                        throw sqlException;
                    }
                    else
                    {
                        throw new HttpException(SR.Cant_connect_sql_session_database, ex);
                    }
                }
            }
        }

        public void DeleteExpiredSessions()
        {
            using (var connection = new SqlConnection(_connectString))
            {
                var cmd = _commandHelper.CreateDeleteExpiredSessionsCmd(DeleteExpiredSessionsSql);
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
                cmd = _commandHelper.CreateGetStateItemExclusiveCmd(GetStateItemExclusiveSql, id);
            }
            else
            {
                cmd = _commandHelper.CreateGetStateItemCmd(GetStateItemSql, id);
            }

            using (var connection = new SqlConnection(_connectString))
            {
                using (var reader = await SqlSessionStateRepositoryUtil.SqlExecuteReaderWithRetryAsync(connection, cmd, CanRetryAsync))
                {
                    if (await reader.ReadAsync())
                    {
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

        public async Task CreateOrUpdateSessionStateItemAsync(bool newItem, string id, 
            byte[] buf, int length, int timeout, int lockCookie, int orginalStreamLen)
        {
            SqlCommand cmd;

            if (!newItem)
            {
                cmd = _commandHelper.CreateUpdateStateItemLongCmd(UpdateStateItemLongSql, id, buf, length, timeout, lockCookie);
            }
            else
            {
                cmd = _commandHelper.CreateInsertStateItemLongCmd(InsertStateItemLongSql, id, buf, length, timeout);
            }

            using (var connection = new SqlConnection(_connectString))
            {
                await SqlSessionStateRepositoryUtil.SqlExecuteNonQueryWithRetryAsync(connection, cmd, CanRetryAsync, newItem);
            }
        }

        public async Task ResetSessionItemTimeoutAsync(string id)
        {
            var cmd = _commandHelper.CreateResetItemTimeoutCmd(ResetItemTimeoutSql, id);
            using (var connection = new SqlConnection(_connectString))
            {
                await SqlSessionStateRepositoryUtil.SqlExecuteNonQueryWithRetryAsync(connection, cmd, CanRetryAsync);
            }
        }

        public async Task RemoveSessionItemAsync(string id, object lockId)
        {
            var cmd = _commandHelper.CreateRemoveStateItemCmd(RemoveStateItemSql, id, lockId);
            using (var connection = new SqlConnection(_connectString))
            {
                await SqlSessionStateRepositoryUtil.SqlExecuteNonQueryWithRetryAsync(connection, cmd, CanRetryAsync);
            }
        }

        public async Task ReleaseSessionItemAsync(string id, object lockId)
        {
            var cmd = _commandHelper.CreateReleaseItemExclusiveCmd(ReleaseItemExclusiveSql, id, lockId);
            using (var connection = new SqlConnection(_connectString))
            {
                await SqlSessionStateRepositoryUtil.SqlExecuteNonQueryWithRetryAsync(connection, cmd, CanRetryAsync);
            }
        }

        public async Task CreateUninitializedSessionItemAsync(string id, int length, byte[] buf, int timeout)
        {
            var cmd = _commandHelper.CreateTempInsertUninitializedItemCmd(TempInsertUninitializedItemSql, id, length, buf, timeout);
            using (var connection = new SqlConnection(_connectString))
            {
                await SqlSessionStateRepositoryUtil.SqlExecuteNonQueryWithRetryAsync(connection, cmd, CanRetryAsync, true);
            }
        }

        private Task<bool> CanRetryAsync(RetryCheckParameter parameter)
        {
            if (parameter.RetryCount >= _maxRetryNum ||
                !ShouldUseInMemoryTableRetry(parameter.Exception))
            {
                return Task.FromResult(false);
            }

            return WaitToRetryAsync(parameter, _retryIntervalMilSec);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task<bool> WaitToRetryAsync(RetryCheckParameter parameter, int retryIntervalMilSec)
        {
            // this actually may sleep up to 15ms
            // but it's better than spinning CPU
            await Task.Delay(retryIntervalMilSec);
            parameter.RetryCount++;

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ShouldUseInMemoryTableRetry(SqlException ex)
        {
            // Error code is defined on
            // https://docs.microsoft.com/en-us/sql/relational-databases/in-memory-oltp/transactions-with-memory-optimized-tables#conflict-detection-and-retry-logic
            if (ex != null && (ex.Number == 41302 || ex.Number == 41305 || ex.Number == 41325 || ex.Number == 41301 || ex.Number == 41839))
            {
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
