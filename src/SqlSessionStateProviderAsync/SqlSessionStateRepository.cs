// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See the License.txt file in the project root for full license information.

namespace Microsoft.AspNet.SessionState
{
    using Resources;
    using System;
    using System.Data.SqlClient;
    using System.Runtime.CompilerServices;
    using System.Threading.Tasks;
    using System.Web;
    using System.Web.SessionState;

    /// <summary>
    /// SQL server version must be >= 8.0
    /// </summary>
    class SqlSessionStateRepository : ISqlSessionStateRepository
    {
        private const int DEFAULT_RETRY_INTERVAL = 1000;
        private const int DEFAULT_RETRY_NUM = 10;

        private int _retryIntervalMilSec;
        private string _connectString;
        private int _maxRetryNum;
        private int _commandTimeout;
        private SqlCommandHelper _commandHelper;

        #region sql statement
        // SQL server version must be >= 8.0
        #region CreateSessionTable
        private const string CreateSessionTableSql = @"
               IF NOT EXISTS (SELECT * 
                 FROM INFORMATION_SCHEMA.TABLES 
                 WHERE TABLE_NAME = '" + SqlSessionStateRepositoryUtil.TableName + @"')
               BEGIN
                CREATE TABLE " + SqlSessionStateRepositoryUtil.TableName + @" (
                SessionId           nvarchar(88)    NOT NULL PRIMARY KEY,
                Created             datetime        NOT NULL DEFAULT GETUTCDATE(),
                Expires             datetime        NOT NULL,
                LockDate            datetime        NOT NULL,
                LockDateLocal       datetime        NOT NULL,
                LockCookie          int             NOT NULL,
                Timeout             int             NOT NULL,
                Locked              bit             NOT NULL,
                SessionItemLong     image           NULL,
                Flags               int             NOT NULL DEFAULT 0,
                ) 
                CREATE NONCLUSTERED INDEX Index_Expires ON " + SqlSessionStateRepositoryUtil.TableName + @" (Expires)
            END";
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

        #region GetStateItemExclusive
        private const string GetStateItemExclusiveSql = @"
            BEGIN TRAN
                DECLARE @textptr AS varbinary(16)
                DECLARE @length AS int
                DECLARE @now AS datetime
                DECLARE @nowLocal AS datetime
            
                SET @now = GETUTCDATE()
                SET @nowLocal = GETDATE()
            
                UPDATE " + SqlSessionStateRepositoryUtil.TableName + @" WITH (ROWLOCK, XLOCK)
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
                    @textptr = CASE Locked
                        WHEN 0 THEN TEXTPTR(SessionItemLong)
                        ELSE NULL
                        END,
                    @length = CASE Locked
                        WHEN 0 THEN DATALENGTH(SessionItemLong)
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
                IF @length IS NOT NULL BEGIN
                    READTEXT " + SqlSessionStateRepositoryUtil.TableName + @".SessionItemLong @textptr 0 @length
                END
            COMMIT TRAN
            ";
        #endregion

        #region GetStateItem
        private const string GetStateItemSql = @"
            BEGIN TRAN
                DECLARE @textptr AS varbinary(16)
                DECLARE @length AS int
                DECLARE @now AS datetime
                SET @now = GETUTCDATE()

                UPDATE " + SqlSessionStateRepositoryUtil.TableName + @" WITH (XLOCK, ROWLOCK)
                SET Expires = DATEADD(n, Timeout, @now), 
                    " + SqlParameterName.Locked + @" = Locked,
                    " + SqlParameterName.LockAge + @" = DATEDIFF(second, LockDate, @now),
                    " + SqlParameterName.LockCookie + @" = LockCookie,
                    @textptr = CASE @locked
                        WHEN 0 THEN TEXTPTR(SessionItemLong)
                        ELSE NULL
                        END,
                    @length = CASE @locked
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
                    READTEXT " + SqlSessionStateRepositoryUtil.TableName + @".SessionItemLong @textptr 0 @length
                END
            COMMIT TRAN
              ";
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
            UPDATE " + SqlSessionStateRepositoryUtil.TableName + @" WITH (ROWLOCK)
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
                FROM " + SqlSessionStateRepositoryUtil.TableName + @" WITH (READUNCOMMITTED)
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
        #endregion

        public SqlSessionStateRepository(string connectionString, int commandTimeout,
            int? retryInterval = DEFAULT_RETRY_INTERVAL, int? retryNum = DEFAULT_RETRY_NUM)
        {
            this._retryIntervalMilSec = retryInterval.HasValue ? retryInterval.Value : DEFAULT_RETRY_INTERVAL;
            this._connectString = connectionString;
            this._maxRetryNum = retryNum.HasValue ? retryNum.Value : DEFAULT_RETRY_NUM;
            this._commandTimeout = commandTimeout;
            this._commandHelper = new SqlCommandHelper(commandTimeout);
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

        #region ISqlSessionStateRepository implementation
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
                    throw new HttpException(SR.Cant_connect_sql_session_database, ex);
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

                if (buf == null)
                {
                    buf = (byte[])cmd.GetOutPutParameterValue(SqlParameterName.SessionItemLong).Value;
                }

                return new SessionItem(buf, true, lockAge, lockId, actions);
            }
        }

        public async Task CreateOrUpdateSessionStateItemAsync(bool newItem, string id, byte[] buf, int length, int timeout, int lockCookie, int orginalStreamLen)
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
        #endregion

    }
}