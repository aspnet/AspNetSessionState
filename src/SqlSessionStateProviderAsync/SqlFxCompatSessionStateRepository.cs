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
    using System.Globalization;

    /// <summary>
    /// SQL server version must be >= 8.0
    /// </summary>
    class SqlFxCompatSessionStateRepository : ISqlSessionStateRepository
    {
        private enum SessionTableType
        {
            None = 0,
            Fx7 = 1,
            Fx8 = 2,
            Async11 = 3,
            Async20 = 4,
            Unknown = -1
        }

        private const int DEFAULT_RETRY_INTERVAL = 1000;
        private const int DEFAULT_RETRY_NUM = 10;
        private readonly string SessionTableName = "ASPStateTempSessions";

        private int _retryIntervalMilSec;
        private string _connectString;
        private int _maxRetryNum;
        private int _commandTimeout;
        private SessionTableType _tableType = SessionTableType.Unknown;
        private SqlCommandHelper _commandHelper;

        #region sql statement
        #region QuerySessionTableSql
        // Tables already exist. But could be one of a couple different flavors. This query does not create,
        // but instead determines which version of state table we are working with, so we can use the
        // correct SProc for accessing the data. We started using different table names in V2.0 of this
        // package. To differentiate the flavors of 'ASPStateTempSessions' however:
        //     0) There is no 'ASPStateTempSessions' table
        //     1) No 'LockDateLocal' field => regsql created for SQL7 and older
        //     2) 'SessionItemShort' exists => regsql created for SQL8 and newer ('SessionItemLong' is always type 'image')
        //        No 'SessionItemShort' field =>
        //     3)     'SessionItemLong' is type 'image' => Regular SQL provider from v1.1 of this async package (2.0 uses 'varbinary'... but also changed table names.)
        //     4)     'SessionItemLong' is type 'varbinary' => In-Memory SQL Table provider (2.0 changed table names, but 1.1 still used 'ASPStateTempSessions'.)
        // This repository only provides compatibility with 1, 2, and 3. The last flavor of 'ASPStateTempSessions' should migrate to the V2.0 style.
        private string QuerySessionTableSql;
        private static readonly string QuerySessionTableTemplate = @"
                BEGIN
                    DECLARE @tableType int
                    DECLARE @isMemoryOpt int
                    SET @tableType = " + (int)SessionTableType.None + @"
                    SET @isMemoryOpt = 0
                    IF EXISTS (SELECT * 
                        FROM INFORMATION_SCHEMA.TABLES 
                        WHERE TABLE_NAME = '{0}')
                    BEGIN
                        IF NOT EXISTS (SELECT * 
                            FROM INFORMATION_SCHEMA.COLUMNS 
                            WHERE TABLE_NAME = '{0}' AND COLUMN_NAME = 'LockDateLocal')
                                SET @tableType = " + (int)SessionTableType.Fx7 + @"
                        ELSE IF EXISTS (SELECT * 
                            FROM INFORMATION_SCHEMA.COLUMNS 
                            WHERE TABLE_NAME = '{0}' AND COLUMN_NAME = 'SessionItemShort')
                                SET @tableType = " + (int)SessionTableType.Fx8 + @"
                        ELSE IF EXISTS(SELECT * 
                            FROM INFORMATION_SCHEMA.COLUMNS 
                            WHERE TABLE_NAME = '{0}' AND COLUMN_NAME = 'SessionItemLong' AND DATA_TYPE = 'image')
                                SET @tableType = " + (int)SessionTableType.Async11 + @"
                        ELSE IF EXISTS(SELECT * 
                            FROM INFORMATION_SCHEMA.COLUMNS 
                            WHERE TABLE_NAME = '{0}' AND COLUMN_NAME = 'SessionItemLong' AND DATA_TYPE = 'varbinary')
                            BEGIN
                                SET @tableType = " + (int)SessionTableType.Async20 + @"
                                BEGIN TRY
                                    SELECT @isMemoryOpt = CASE durability
                                        WHEN 0 THEN 2
                                        ELSE 1
                                        END
                                    FROM sys.tables
                                    WHERE is_memory_optimized = 1 AND name = '{0}'
                                END TRY
                                BEGIN CATCH
                                   SET @isMemoryOpt = 0
                                END CATCH
                            END
                        ELSE
                            SET @tableType = " + (int)SessionTableType.Unknown + @"
                    END

                    SELECT @tableType, @isMemoryOpt
                END
            ";
        #endregion

        // The FX* tables will use the SProcs installed by aspnet_regsql. (System\Web\State\sqlstateclientmanager.cs describes them well.)
        // For Async1.1, these SQL statements were what this package used previously. There were no SProcs created alongside the Asycn1.1 table.
        //      In the interest of not upsetting compatability, they have not been converted to SProcs. Use Async V2 for that update.
        #region Async11 SQL
        #region GetStateItemExclusive
        private string GetStateItemExclusiveSql;
        private static readonly string GetStateItemExclusiveTemplate = @"
            BEGIN TRAN
                DECLARE @textptr AS varbinary(16)
                DECLARE @length AS int
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
                    READTEXT {0}.SessionItemLong @textptr 0 @length
                END
            COMMIT TRAN
            ";
        #endregion

        #region GetStateItem
        private string GetStateItemSql;
        private static readonly string GetStateItemTemplate = @"
            BEGIN TRAN
                DECLARE @textptr AS varbinary(16)
                DECLARE @length AS int
                DECLARE @now AS datetime
                SET @now = GETUTCDATE()

                UPDATE {0} WITH (XLOCK, ROWLOCK)
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
                    READTEXT {0}.SessionItemLong @textptr 0 @length
                END
            COMMIT TRAN
              ";
        #endregion

        #region DeleteExpiredSessions
        private string DeleteExpiredSessionsSql;
        private static readonly string DeleteExpiredSessionsTemplate = @"
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

            DROP TABLE #tblExpiredSessions";
        #endregion

        #region InsertStateItem
        private string InsertStateItemSql;
        private static readonly string InsertStateItemTemplate = @"
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
                 1)";
        #endregion

        #region InsertUninitializedItem
        private string InsertUninitializedItemSql;
        private static readonly string InsertUninitializedItemTemplate = @"
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
                 1)";
        #endregion

        #region ReleaseItemExclusive
        private string ReleaseItemExclusiveSql;
        private static readonly string ReleaseItemExclusiveTemplate = @"
            UPDATE {0}
            SET Expires = DATEADD(n, Timeout, GETUTCDATE()),
                Locked = 0
            WHERE SessionId = " + SqlParameterName.SessionId + @" AND LockCookie = " + SqlParameterName.LockCookie;
        #endregion

        #region RemoveStateItem
        private string RemoveStateItemSql;
        private static readonly string RemoveStateItemTemplate = @"
            DELETE {0}
            WHERE SessionId = " + SqlParameterName.SessionId + @" AND LockCookie = " + SqlParameterName.LockCookie;
        #endregion

        #region ResetItemTimeout
        private string ResetItemTimeoutSql;
        private static readonly string ResetItemTimeoutTemplate = @"
            UPDATE {0}
            SET Expires = DATEADD(n, Timeout, GETUTCDATE())
            WHERE SessionId = " + SqlParameterName.SessionId;
        #endregion

        #region UpdateStateItem
        private string UpdateStateItemSql;
        private const string UpdateStateItemTemplate = @"
            UPDATE {0} WITH (ROWLOCK)
            SET Expires = DATEADD(n, " + SqlParameterName.Timeout + @", GETUTCDATE()), 
                SessionItemLong = " + SqlParameterName.SessionItemLong + @",
                Timeout = " + SqlParameterName.Timeout + @",
                Locked = 0
            WHERE SessionId = " + SqlParameterName.SessionId + @" AND LockCookie = " + SqlParameterName.LockCookie;
        #endregion

        #endregion
        #endregion

        public SqlFxCompatSessionStateRepository(string connectionString, string sessionTableName, int commandTimeout,
            int? retryInterval = DEFAULT_RETRY_INTERVAL, int? retryNum = DEFAULT_RETRY_NUM)
        {
            this._retryIntervalMilSec = retryInterval.HasValue ? retryInterval.Value : DEFAULT_RETRY_INTERVAL;
            this._connectString = connectionString;
            this._maxRetryNum = retryNum.HasValue ? retryNum.Value : DEFAULT_RETRY_NUM;
            this._commandTimeout = commandTimeout;
            this._commandHelper = new SqlCommandHelper(commandTimeout);

            // Unlike the other repositories, we don't create our own tables/sprocs. But it is possible for aspnet_regsql to
            // create session table with custom name, so we allow for that. However, the SProc names are always the same
            // when created by aspnet_regsql (and no SProcs are ever created for Async 1.1), so we don't need to update
            // anything other than table name here.
            if (!String.IsNullOrWhiteSpace(sessionTableName))
                SessionTableName = sessionTableName;

            // Create SQL commands from templates once. (This constructor is 1-time-init enforced by SqlSessionStateProviderAsync.Initialize)
            QuerySessionTableSql = String.Format(QuerySessionTableTemplate, SessionTableName);
            GetStateItemExclusiveSql = String.Format(GetStateItemExclusiveTemplate, SessionTableName);
            GetStateItemSql = String.Format(GetStateItemTemplate, SessionTableName);
            DeleteExpiredSessionsSql = String.Format(DeleteExpiredSessionsTemplate, SessionTableName);
            InsertStateItemSql = String.Format(InsertStateItemTemplate, SessionTableName);
            InsertUninitializedItemSql = String.Format(InsertUninitializedItemTemplate, SessionTableName);
            ReleaseItemExclusiveSql = String.Format(ReleaseItemExclusiveTemplate, SessionTableName);
            RemoveStateItemSql = String.Format(RemoveStateItemTemplate, SessionTableName);
            ResetItemTimeoutSql = String.Format(ResetItemTimeoutTemplate, SessionTableName);
            UpdateStateItemSql = String.Format(UpdateStateItemTemplate, SessionTableName);
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

        internal string SessionTableTypeString
        {
            get { return _tableType.ToString(); }
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
            // There is no creating of tables with this compat-focused repository. They should already be
            // created by aspnet_regsql.exe for use with the older in-box SQL State Provider or the previous 1.1
            // version of this package. But we need to know which version of the state database we are working
            // with. We can query that here at the start.
            using (var connection = new SqlConnection(_connectString))
            {
                try
                {
                    var cmd = _commandHelper.CreateSqlCommand(QuerySessionTableSql);

                    using (var reader = await SqlSessionStateRepositoryUtil.SqlExecuteReaderWithRetryAsync(connection, cmd, CanRetryAsync))
                    {
                        if (await reader.ReadAsync())
                        {
                            _tableType = await reader.GetFieldValueAsync<SessionTableType>(0);
                            int isMemoryOptimized = await reader.GetFieldValueAsync<int>(1);

                            // Make sure it's one of the table types we can handle
                            switch (_tableType)
                            {
                                case SessionTableType.Fx7:
                                case SessionTableType.Fx8:
                                case SessionTableType.Async11:
                                    break;

                                case SessionTableType.None:
                                    throw new HttpException(String.Format(CultureInfo.CurrentCulture, SR.SessionTable_not_found, SessionTableName, RepositoryType.FrameworkCompat.ToString()));

                                case SessionTableType.Async20:
                                    RepositoryType betterType = RepositoryType.SqlServer;
                                    if (isMemoryOptimized == 1) { betterType = RepositoryType.InMemory; }
                                    else if (isMemoryOptimized == 2) { betterType = RepositoryType.InMemoryDurable; }
                                    throw new HttpException(String.Format(CultureInfo.CurrentCulture, SR.SessionTable_current, SessionTableName, betterType.ToString()));

                                case SessionTableType.Unknown:
                                default:
                                    throw new HttpException(String.Format(CultureInfo.CurrentCulture, SR.SessionTable_unknown, SessionTableName));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new HttpException(SR.Cant_connect_sql_session_database, ex);
                }
            }

            return true;
        }

        public void DeleteExpiredSessions()
        {
            using (var connection = new SqlConnection(_connectString))
            {
                SqlCommand cmd = null;

                switch (_tableType)
                {
                    // Use SProcs installed by aspnet_regsql
                    case SessionTableType.Fx7:
                    case SessionTableType.Fx8:
                        cmd = _commandHelper.CreateSqlCommandForSP("DeleteExpiredSessions");
                        break;

                    // Use internal SQL statements
                    case SessionTableType.Async11:
                        cmd = _commandHelper.CreateSqlCommand(DeleteExpiredSessionsSql);
                        break;
                }
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
                switch (_tableType)
                {
                    // Use SProcs installed by aspnet_regsql
                    case SessionTableType.Fx7:
                        cmd = _commandHelper.CreateSqlCommandForSP("TempGetStateItemExclusive3");
                        cmd.Parameters.AddFxSessionIdParameter(id)
                                      .AddItemShortParameter()
                                      .AddLockedParameter()
                                      .AddLockDateParameter()
                                      .AddLockCookieParameter()
                                      .AddActionFlagsParameter();
                        break;
                    case SessionTableType.Fx8:
                        cmd = _commandHelper.CreateSqlCommandForSP("TempGetStateItemExclusive3");
                        cmd.Parameters.AddFxSessionIdParameter(id)
                                      .AddItemShortParameter()
                                      .AddLockedParameter()
                                      .AddLockAgeParameter()
                                      .AddLockCookieParameter()
                                      .AddActionFlagsParameter();
                        break;

                    // Use internal SQL statements
                    case SessionTableType.Async11:
                        cmd = _commandHelper.CreateSqlCommand(GetStateItemExclusiveSql);
                        cmd.Parameters.AddSessionIdParameter(id)
                                      .AddLockAgeParameter()
                                      .AddLockedParameter()
                                      .AddLockCookieParameter()
                                      .AddActionFlagsParameter();
                        break;
                }
            }
            else
            {
                switch (_tableType)
                {
                    // Use SProcs installed by aspnet_regsql
                    case SessionTableType.Fx7:
                        cmd = _commandHelper.CreateSqlCommandForSP("TempGetStateItem3");
                        cmd.Parameters.AddFxSessionIdParameter(id)
                                      .AddItemShortParameter()
                                      .AddLockedParameter()
                                      .AddLockDateParameter()
                                      .AddLockCookieParameter()
                                      .AddActionFlagsParameter();
                        break;
                    case SessionTableType.Fx8:
                        cmd = _commandHelper.CreateSqlCommandForSP("TempGetStateItem3");
                        cmd.Parameters.AddFxSessionIdParameter(id)
                                      .AddItemShortParameter()
                                      .AddLockedParameter()
                                      .AddLockAgeParameter()
                                      .AddLockCookieParameter()
                                      .AddActionFlagsParameter();
                        break;

                    // Use internal SQL statements
                    case SessionTableType.Async11:
                        cmd = _commandHelper.CreateSqlCommand(GetStateItemSql);
                        cmd.Parameters.AddSessionIdParameter(id)
                                      .AddLockAgeParameter()
                                      .AddLockedParameter()
                                      .AddLockCookieParameter()
                                      .AddActionFlagsParameter();
                        break;
                }
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

                if (buf == null && (_tableType == SessionTableType.Fx7 || _tableType == SessionTableType.Fx8))
                {
                    buf = (byte[])cmd.GetOutPutParameterValue(SqlParameterName.ItemShort).Value;
                }

                return new SessionItem(buf, true, lockAge, lockId, actions);
            }
        }

        public async Task CreateOrUpdateSessionStateItemAsync(bool newItem, string id, byte[] buf, int length, int timeout, int lockCookie, int originalStreamLen)
        {
            SqlCommand cmd = null;

            if (!newItem)
            {
                switch (_tableType)
                {
                    // Use SProcs installed by aspnet_regsql
                    case SessionTableType.Fx7:
                    case SessionTableType.Fx8:
                        // The aspnet_regiis tables have to keep order with short vs long
                        if (length <= SqlSessionStateRepositoryUtil.ITEM_SHORT_LENGTH)
                        {
                            // New item is short. Choose command name based on old length
                            if (originalStreamLen <= SqlSessionStateRepositoryUtil.ITEM_SHORT_LENGTH)
                                cmd = _commandHelper.CreateSqlCommandForSP("TempUpdateStateItemShort");
                            else
                                cmd = _commandHelper.CreateSqlCommandForSP("TempUpdateStateItemShortNullLong");
                            cmd.Parameters.AddFxSessionIdParameter(id)
                                          .AddItemShortParameter(length, buf)
                                          .AddTimeoutParameter(timeout)
                                          .AddLockCookieParameter(lockCookie);
                        }
                        else
                        {
                            // New item is long. Choose command name based on old length
                            if (originalStreamLen <= SqlSessionStateRepositoryUtil.ITEM_SHORT_LENGTH)
                                cmd = _commandHelper.CreateSqlCommandForSP("TempUpdateStateItemLong");
                            else
                                cmd = _commandHelper.CreateSqlCommandForSP("TempUpdateStateItemLongNullShort");
                            cmd.Parameters.AddFxSessionIdParameter(id)
                                          .AddItemLongParameter(length, buf)
                                          .AddTimeoutParameter(timeout)
                                          .AddLockCookieParameter(lockCookie);
                        }
                        break;

                    // Use internal SQL statements
                    case SessionTableType.Async11:
                        cmd = _commandHelper.CreateSqlCommand(UpdateStateItemSql);
                        cmd.Parameters.AddSessionIdParameter(id)
                                      .AddSessionItemLongImageParameter(length, buf)
                                      .AddTimeoutParameter(timeout)
                                      .AddLockCookieParameter(lockCookie);
                        break;
                }
            }
            else
            {
                switch (_tableType)
                {
                    // Use SProcs installed by aspnet_regsql
                    case SessionTableType.Fx7:
                    case SessionTableType.Fx8:
                        // The aspnet_regiis tables have to keep order with short vs long
                        if (length <= SqlSessionStateRepositoryUtil.ITEM_SHORT_LENGTH)
                        {
                            cmd = _commandHelper.CreateSqlCommandForSP("TempInsertStateItemShort");
                            cmd.Parameters.AddFxSessionIdParameter(id)
                                          .AddItemShortParameter(length, buf)
                                          .AddTimeoutParameter(timeout);
                        }
                        else
                        {
                            cmd = _commandHelper.CreateSqlCommandForSP("TempInsertStateItemLong");
                            cmd.Parameters.AddFxSessionIdParameter(id)
                                          .AddItemLongParameter(length, buf)
                                          .AddTimeoutParameter(timeout);
                        }
                        break;

                    // Use internal SQL statements
                    case SessionTableType.Async11:
                        cmd = _commandHelper.CreateSqlCommand(InsertStateItemSql);
                        cmd.Parameters.AddSessionIdParameter(id)
                                      .AddSessionItemLongImageParameter(length, buf)
                                      .AddTimeoutParameter(timeout);
                        break;
                }
            }

            using (var connection = new SqlConnection(_connectString))
            {
                await SqlSessionStateRepositoryUtil.SqlExecuteNonQueryWithRetryAsync(connection, cmd, CanRetryAsync, newItem);
            }
        }

        public async Task ResetSessionItemTimeoutAsync(string id)
        {
            SqlCommand cmd = null;

            switch (_tableType)
            {
                // Use SProcs installed by aspnet_regsql
                case SessionTableType.Fx7:
                case SessionTableType.Fx8:
                    cmd = _commandHelper.CreateSqlCommandForSP("TempResetTimeout");
                    cmd.Parameters.AddFxSessionIdParameter(id);
                    break;

                // Use internal SQL statements
                case SessionTableType.Async11:
                    cmd = _commandHelper.CreateSqlCommand(ResetItemTimeoutSql);
                    cmd.Parameters.AddSessionIdParameter(id);
                    break;
            }

            using (var connection = new SqlConnection(_connectString))
            {
                await SqlSessionStateRepositoryUtil.SqlExecuteNonQueryWithRetryAsync(connection, cmd, CanRetryAsync);
            }
        }

        public async Task RemoveSessionItemAsync(string id, object lockId)
        {
            SqlCommand cmd = null;

            switch (_tableType)
            {
                // Use SProcs installed by aspnet_regsql
                case SessionTableType.Fx7:
                case SessionTableType.Fx8:
                    cmd = _commandHelper.CreateSqlCommandForSP("TempRemoveStateItem");
                    cmd.Parameters.AddFxSessionIdParameter(id)
                                  .AddLockCookieParameter(lockId);
                    break;

                // Use internal SQL statements
                case SessionTableType.Async11:
                    cmd = _commandHelper.CreateSqlCommand(RemoveStateItemSql);
                    cmd.Parameters.AddSessionIdParameter(id)
                                  .AddLockCookieParameter(lockId);
                    break;
            }

            using (var connection = new SqlConnection(_connectString))
            {
                await SqlSessionStateRepositoryUtil.SqlExecuteNonQueryWithRetryAsync(connection, cmd, CanRetryAsync);
            }
        }

        public async Task ReleaseSessionItemAsync(string id, object lockId)
        {
            SqlCommand cmd = null;

            switch (_tableType)
            {
                // Use SProcs installed by aspnet_regsql
                case SessionTableType.Fx7:
                case SessionTableType.Fx8:
                    cmd = _commandHelper.CreateSqlCommandForSP("TempReleaseStateItemExclusive");
                    cmd.Parameters.AddFxSessionIdParameter(id)
                                  .AddLockCookieParameter(lockId);
                    break;

                // Use internal SQL statements
                case SessionTableType.Async11:
                    cmd = _commandHelper.CreateSqlCommand(ReleaseItemExclusiveSql);
                    cmd.Parameters.AddSessionIdParameter(id)
                                  .AddLockCookieParameter(lockId);
                    break;
            }

            using (var connection = new SqlConnection(_connectString))
            {
                await SqlSessionStateRepositoryUtil.SqlExecuteNonQueryWithRetryAsync(connection, cmd, CanRetryAsync);
            }
        }

        public async Task CreateUninitializedSessionItemAsync(string id, int length, byte[] buf, int timeout)
        {
            SqlCommand cmd = null;

            switch (_tableType)
            {
                // Use SProcs installed by aspnet_regsql
                case SessionTableType.Fx7:
                case SessionTableType.Fx8:
                    cmd = _commandHelper.CreateSqlCommandForSP("TempInsertUninitializedItem");
                    cmd.Parameters.AddFxSessionIdParameter(id)
                                  .AddItemShortParameter(length, buf) // TODO - Fx just assumed this was "short" - in code and SProc.
                                  .AddTimeoutParameter(timeout);
                    break;

                // Use internal SQL statements
                case SessionTableType.Async11:
                    cmd = _commandHelper.CreateSqlCommand(InsertUninitializedItemSql);
                    cmd.Parameters.AddSessionIdParameter(id)
                                  .AddSessionItemLongImageParameter(length, buf)
                                  .AddTimeoutParameter(timeout);
                    break;
            }

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