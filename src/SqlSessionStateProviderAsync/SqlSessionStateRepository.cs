namespace Microsoft.AspNet.SessionState
{
    using Resources;
    using System;
    using System.Data;
    using System.Data.SqlClient;
    using System.Threading.Tasks;
    using System.Web;
    using System.Web.SessionState;

    /// <summary>
    /// SQL server version must be >= 8.0
    /// </summary>
    class SqlSessionStateRepository : ISqlSessionStateRepository
    {
        private int commandTimeout;

        #region sql statement
        // SQL server version must be >= 8.0
        #region CreateSessionTable
        private static readonly string CreateSessionTableSql = $@"
               IF NOT EXISTS (SELECT * 
                 FROM INFORMATION_SCHEMA.TABLES 
                 WHERE TABLE_NAME = '{SqlSessionStateRepositoryUtil.TableName}')
               BEGIN
                CREATE TABLE {SqlSessionStateRepositoryUtil.TableName} (
                SessionId           nvarchar(88)    NOT NULL PRIMARY KEY,
                Created             datetime        NOT NULL DEFAULT GETUTCDATE(),
                Expires             datetime        NOT NULL,
                LockDate            datetime        NOT NULL,
                LockDateLocal       datetime        NOT NULL,
                LockCookie          int             NOT NULL,
                Timeout             int             NOT NULL,
                Locked              bit             NOT NULL,
                SessionItemShort    varbinary({SqlSessionStateRepositoryUtil.ItemShortLength}) NULL,
                SessionItemLong     image           NULL,
                Flags               int             NOT NULL DEFAULT 0,
                ) 
                CREATE NONCLUSTERED INDEX Index_Expires ON {SqlSessionStateRepositoryUtil.TableName} (Expires)
            END";
            #endregion

        #region TempInsertUninitializedItem
            private static readonly string TempInsertUninitializedItemSql = $@"
            DECLARE @now AS datetime
            DECLARE @nowLocal AS datetime
            SET @now = GETUTCDATE()
            SET @nowLocal = GETDATE()

            INSERT {SqlSessionStateRepositoryUtil.TableName} (SessionId, 
                 SessionItemShort, 
                 Timeout, 
                 Expires, 
                 Locked, 
                 LockDate,
                 LockDateLocal,
                 LockCookie,
                 Flags) 
            VALUES
                (@{SqlParameterName.SessionId},
                 @{SqlParameterName.SessionItemShort},
                 @{SqlParameterName.Timeout},
                 DATEADD(n, @{SqlParameterName.Timeout}, @now),
                 0,
                 @now,
                 @nowLocal,
                 1,
                 1)";
            #endregion

        #region GetStateItemExclusive
            private static readonly string GetStateItemExclusiveSql = $@"
            DECLARE @textptr AS varbinary(16)
            DECLARE @length AS int
            DECLARE @now AS datetime
            DECLARE @nowLocal AS datetime
            
            SET @now = GETUTCDATE()
            SET @nowLocal = GETDATE()
            
            UPDATE {SqlSessionStateRepositoryUtil.TableName}
            SET Expires = DATEADD(n, Timeout, @now), 
                LockDate = CASE Locked
                    WHEN 0 THEN @now
                    ELSE LockDate
                    END,
                LockDateLocal = CASE Locked
                    WHEN 0 THEN @nowLocal
                    ELSE LockDateLocal
                    END,
                @{SqlParameterName.LockAge} = CASE Locked
                    WHEN 0 THEN 0
                    ELSE DATEDIFF(second, LockDate, @now)
                    END,
                @{SqlParameterName.LockCookie} = LockCookie = CASE Locked
                    WHEN 0 THEN LockCookie + 1
                    ELSE LockCookie
                    END,
                @{SqlParameterName.SessionItemShort} = CASE Locked
                    WHEN 0 THEN SessionItemShort
                    ELSE NULL
                    END,
                @textptr = CASE Locked
                    WHEN 0 THEN TEXTPTR(SessionItemLong)
                    ELSE NULL
                    END,
                @length = CASE Locked
                    WHEN 0 THEN DATALENGTH(SessionItemLong)
                    ELSE NULL
                    END,
                @{SqlParameterName.Locked} = Locked,
                Locked = 1,

                /* If the Uninitialized flag (0x1) if it is set,
                   remove it and return InitializeItem (0x1) in actionFlags */
                Flags = CASE
                    WHEN (Flags & 1) <> 0 THEN (Flags & ~1)
                    ELSE Flags
                    END,
                @{SqlParameterName.ActionFlags} = CASE
                    WHEN (Flags & 1) <> 0 THEN 1
                    ELSE 0
                    END
            WHERE SessionId = @{SqlParameterName.SessionId}
            IF @length IS NOT NULL BEGIN
                READTEXT {SqlSessionStateRepositoryUtil.TableName}.SessionItemLong @textptr 0 @length
            END";
        #endregion

        #region GetStateItem
        private static readonly string GetStateItemSql = $@"
            DECLARE @textptr AS varbinary(16)
            DECLARE @length AS int
            DECLARE @now AS datetime
            SET @now = GETUTCDATE()

            UPDATE {SqlSessionStateRepositoryUtil.TableName}
            SET Expires = DATEADD(n, Timeout, @now), 
                @{SqlParameterName.Locked} = Locked,
                @{SqlParameterName.LockAge} = DATEDIFF(second, LockDate, @now),
                @{SqlParameterName.LockCookie} = LockCookie,
                @{SqlParameterName.SessionItemShort} = CASE @locked
                    WHEN 0 THEN SessionItemShort
                    ELSE NULL
                    END,
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
                @{SqlParameterName.ActionFlags} = CASE
                    WHEN (Flags & 1) <> 0 THEN 1
                    ELSE 0
                    END
            WHERE SessionId = @{SqlParameterName.SessionId}
            IF @length IS NOT NULL BEGIN
                READTEXT {SqlSessionStateRepositoryUtil.TableName}.SessionItemLong @textptr 0 @length
            END";
        #endregion

        #region ReleaseItemExclusive
        private static readonly string ReleaseItemExclusiveSql = $@"
            UPDATE {SqlSessionStateRepositoryUtil.TableName}
            SET Expires = DATEADD(n, Timeout, GETDATE()),
                Locked = 0
            WHERE SessionId = @{SqlParameterName.SessionId} AND LockCookie = @{SqlParameterName.LockCookie}";
        #endregion

        #region RemoveStateItem
        private static readonly string RemoveStateItemSql = $@"
            DELETE {SqlSessionStateRepositoryUtil.TableName}
            WHERE SessionId = @{SqlParameterName.SessionId} AND LockCookie = @{SqlParameterName.LockCookie}";
        #endregion

        #region ResetItemTimeout
        private static readonly string ResetItemTimeoutSql = $@"
            UPDATE {SqlSessionStateRepositoryUtil.TableName}
            SET Expires = DATEADD(n, Timeout, GETUTCDATE())
            WHERE SessionId = @{SqlParameterName.SessionId}";
        #endregion

        #region UpdateStateItemShort
        private static readonly string UpdateStateItemShortSql = $@"
            UPDATE {SqlSessionStateRepositoryUtil.TableName}
            SET Expires = DATEADD(n, @{SqlParameterName.Timeout}, GETUTCDATE()), 
                SessionItemShort = @{SqlParameterName.SessionItemShort}, 
                Timeout = @{SqlParameterName.Timeout},
                Locked = 0
            WHERE SessionId = @{SqlParameterName.SessionId} AND LockCookie = @{SqlParameterName.LockCookie}";
        #endregion

        #region UpdateStateItemShortNullLong
        private static readonly string UpdateStateItemShortNullLongSql = $@"
            UPDATE {SqlSessionStateRepositoryUtil.TableName}
            SET Expires = DATEADD(n, @{SqlParameterName.Timeout}, GETDATE()), 
                SessionItemShort = @{SqlParameterName.SessionItemShort}, 
                SessionItemLong = NULL, 
                Timeout = @{SqlParameterName.Timeout},
                Locked = 0
            WHERE SessionId = @{SqlParameterName.SessionId} AND LockCookie = @{SqlParameterName.LockCookie}";
        #endregion

        #region UpdateStateItemLongNullShort
        private static readonly string UpdateStateItemLongNullShortSql = $@"
            UPDATE {SqlSessionStateRepositoryUtil.TableName}
            SET Expires = DATEADD(n, @{SqlParameterName.Timeout}, GETUTCDATE()), 
                SessionItemLong = @{SqlParameterName.SessionItemLong}, 
                SessionItemShort = NULL,
                Timeout = @{SqlParameterName.Timeout},
                Locked = 0
            WHERE SessionId = @{SqlParameterName.SessionId} AND LockCookie = @{SqlParameterName.LockCookie}";
        #endregion

        #region UpdateStateItemLong
        private static readonly string UpdateStateItemLongSql = $@"
            UPDATE {SqlSessionStateRepositoryUtil.TableName}
            SET Expires = DATEADD(n, @{SqlParameterName.Timeout}, GETUTCDATE()), 
                SessionItemLong = @{SqlParameterName.SessionItemLong},
                Timeout = @{SqlParameterName.Timeout},
                Locked = 0
            WHERE SessionId = @{SqlParameterName.SessionId} AND LockCookie = @{SqlParameterName.LockCookie}";
        #endregion

        #region InsertStateItemShort
        private static readonly string InsertStateItemShortSql = $@"
            DECLARE @now AS datetime
            DECLARE @nowLocal AS datetime
            
            SET @now = GETUTCDATE()
            SET @nowLocal = GETDATE()

            INSERT {SqlSessionStateRepositoryUtil.TableName} 
                (SessionId, 
                 SessionItemShort, 
                 Timeout, 
                 Expires, 
                 Locked, 
                 LockDate,
                 LockDateLocal,
                 LockCookie) 
            VALUES 
                (@{SqlParameterName.SessionId}, 
                 @{SqlParameterName.SessionItemShort}, 
                 @{SqlParameterName.Timeout}, 
                 DATEADD(n, @{SqlParameterName.Timeout}, @now), 
                 0, 
                 @now,
                 @nowLocal,
                 1)";
        #endregion

        #region InsertStateItemLong
        private static readonly string InsertStateItemLongSql = $@"
            DECLARE @now AS datetime
            DECLARE @nowLocal AS datetime
            
            SET @now = GETUTCDATE()
            SET @nowLocal = GETDATE()

            INSERT {SqlSessionStateRepositoryUtil.TableName} 
                (SessionId, 
                 SessionItemLong, 
                 Timeout, 
                 Expires, 
                 Locked, 
                 LockDate,
                 LockDateLocal,
                 LockCookie) 
            VALUES 
                (@{SqlParameterName.SessionId}, 
                 @{SqlParameterName.SessionItemLong}, 
                 @{SqlParameterName.Timeout}, 
                 DATEADD(n, @{SqlParameterName.Timeout}, @now), 
                 0, 
                 @now,
                 @nowLocal,
                 1)";
        #endregion

        #region DeleteExpiredSessions
        private static readonly string DeleteExpiredSessionsSql = $@"
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
                FROM {SqlSessionStateRepositoryUtil.TableName} WITH (READUNCOMMITTED)
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
                        DELETE FROM {SqlSessionStateRepositoryUtil.TableName} WHERE SessionId = @SessionId AND Expires < @now
                        FETCH NEXT FROM ExpiredSessionCursor INTO @SessionId
                    END

                CLOSE ExpiredSessionCursor

                DEALLOCATE ExpiredSessionCursor
            END 

            DROP TABLE #tblExpiredSessions";
            #endregion
        #endregion

        public SqlSessionStateRepository(int commandTimeout)
        {
            this.commandTimeout = commandTimeout;
        }

        #region ISqlSessionStateRepository implementation
        public void CreateSessionStateTable()
        {
            using (var invoker = new SqlCommandInvoker(CreateCreateSessionTableCmd()))
            {
                try
                {
                    var task = invoker.SqlExecuteNonQueryWithRetryAsync().ConfigureAwait(false);
                    task.GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    // Indicate that the DB doesn't support InMemoryTable
                    var innerException = ex.InnerException as SqlException;
                    if (innerException != null && innerException.Number == 40536)
                    {
                        throw innerException;
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
            using (var invoker = new SqlCommandInvoker(CreateDeleteExpiredSessionsCmd()))
            {
                var task = invoker.SqlExecuteNonQueryWithRetryAsync().ConfigureAwait(false);
                task.GetAwaiter().GetResult();
            }
        }

        public async Task<SessionItem> GetStateIteAsync(string id, bool exclusive)
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
                cmd = CreateGetStateItemExclusiveCmd(id);
            }
            else
            {
                cmd = CreateGetStateItemCmd(id);
            }

            using (var invoker = new SqlCommandInvoker(cmd))
            {
                using (var reader = await invoker.SqlExecuteReaderWithRetryAsync())
                {
                    try
                    {
                        if (await reader.ReadAsync())
                        {
                            buf = await reader.GetFieldValueAsync<byte[]>(0);
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new HttpException(SR.Cant_connect_sql_session_database, ex);
                    }
                }

                var outParameterLocked = invoker.GetOutPutParameterValue(SqlParameterName.Locked);
                if (outParameterLocked == null || Convert.IsDBNull(outParameterLocked.Value))
                {
                    return null;
                }
                locked = (bool)outParameterLocked.Value;
                lockId = (int)invoker.GetOutPutParameterValue(SqlParameterName.LockCookie).Value;

                if (locked)
                {
                    lockAge = new TimeSpan(0, 0, (int)invoker.GetOutPutParameterValue(SqlParameterName.LockAge).Value);

                    if (lockAge > new TimeSpan(0, 0, Sec.ONE_YEAR))
                    {
                        lockAge = TimeSpan.Zero;
                    }
                    return new SessionItem(null, true, lockAge, lockId, actions);
                }
                actions = (SessionStateActions)invoker.GetOutPutParameterValue(SqlParameterName.ActionFlags).Value;

                if (buf == null)
                {
                    buf = (byte[])invoker.GetOutPutParameterValue(SqlParameterName.SessionItemShort).Value;
                }

                return new SessionItem(buf, true, lockAge, lockId, actions);
            }
        }

        public async Task CreateUpdateStateItemAsync(bool newItem, string id, byte[] buf, int length, int timeout, int lockCookie, int orginalStreamLen)
        {
            SqlCommand cmd;

            if (!newItem)
            {
                if (length <= SqlSessionStateRepositoryUtil.ItemShortLength)
                {
                    cmd = orginalStreamLen <= SqlSessionStateRepositoryUtil.ItemShortLength ?
                        CreateUpdateStateItemShortCmd(id, buf, length, timeout, lockCookie) :
                        CreateUpdateStateItemShortNullLongCmd(id, buf, length, timeout, lockCookie);
                }
                else
                {
                    cmd = orginalStreamLen <= SqlSessionStateRepositoryUtil.ItemShortLength ?
                        CreateUpdateStateItemLongNullShortCmd(id, buf, length, timeout, lockCookie) :
                        CreateUpdateStateItemLongCmd(id, buf, length, timeout, lockCookie);
                }
            }
            else
            {
                cmd = length <= SqlSessionStateRepositoryUtil.ItemShortLength ?
                    CreateInsertStateItemShortCmd(id, buf, length, timeout) :
                    CreateInsertStateItemLongCmd(id, buf, length, timeout);
            }

            using (var invoker = new SqlCommandInvoker(cmd))
            {
                await invoker.SqlExecuteNonQueryWithRetryAsync(newItem);
            }
        }

        public async Task ResetSessionItemTimeoutAsync(string id)
        {
            var cmd = CreateResetItemTimeoutCmd(id);
            using (var invoker = new SqlCommandInvoker(cmd))
            {
                await invoker.SqlExecuteNonQueryWithRetryAsync();
            }
        }

        public async Task RemoveSessionItemAsync(string id, object lockId)
        {
            var cmd = CreateRemoveStateItemCmd(id, lockId);
            using (var invoker = new SqlCommandInvoker(cmd))
            {
                await invoker.SqlExecuteNonQueryWithRetryAsync();
            }
        }

        public async Task ReleaseSessionItemAsync(string id, object lockId)
        {
            var cmd = CreateReleaseItemExclusiveCmd(id, lockId);
            using (var invoker = new SqlCommandInvoker(cmd))
            {
                await invoker.SqlExecuteNonQueryWithRetryAsync();
            }
        }

        public async Task CreateUninitializedSessionItemAsync(string id, int length, byte[] buf, int timeout)
        {
            var cmd = CreateTempInsertUninitializedItemCmd(id, length, buf, timeout);
            using (var invoker = new SqlCommandInvoker(cmd))
            {
                await invoker.SqlExecuteNonQueryWithRetryAsync(true);
            }
        }
        #endregion

        protected virtual SqlCommand CreateCreateSessionTableCmd()
        {
            return CreateSqlCommand(CreateSessionTableSql);
        }

        protected virtual SqlCommand CreateTempInsertUninitializedItemCmd(string id, int length, byte[] buf, int timeout)
        {
            var cmd = CreateSqlCommand(TempInsertUninitializedItemSql);
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemShortParameter(length, buf)
                          .AddTimeoutParameter(timeout);

            return cmd;
        }

        protected virtual SqlCommand CreateGetStateItemExclusiveCmd(string id)
        {
            var cmd = CreateSqlCommand(GetStateItemExclusiveSql);
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemShortParameter()
                          .AddLockAgeParameter()
                          .AddLockedParameter()
                          .AddLockCookieParameter()
                          .AddActionFlagsParameter();

            return cmd;
        }

        protected virtual SqlCommand CreateGetStateItemCmd(string id)
        {
            var cmd = CreateSqlCommand(GetStateItemSql);
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemShortParameter()
                          .AddLockedParameter()
                          .AddLockAgeParameter()
                          .AddLockCookieParameter()
                          .AddActionFlagsParameter();

            return cmd;
        }

        protected virtual SqlCommand CreateReleaseItemExclusiveCmd(string id, object lockid)
        {
            var cmd = CreateSqlCommand(ReleaseItemExclusiveSql);
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddLockCookieParameter(lockid);

            return cmd;
        }

        protected virtual SqlCommand CreateRemoveStateItemCmd(string id, object lockid)
        {
            var cmd = CreateSqlCommand(RemoveStateItemSql);
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddLockCookieParameter(lockid);

            return cmd;
        }

        protected virtual SqlCommand CreateResetItemTimeoutCmd(string id)
        {
            var cmd = CreateSqlCommand(ResetItemTimeoutSql);
            cmd.Parameters.AddSessionIdParameter(id);

            return cmd;
        }

        protected virtual SqlCommand CreateUpdateStateItemShortCmd(string id, byte[] buf, int length, int timeout, int lockCookie)
        {
            var cmd = CreateSqlCommand(UpdateStateItemShortSql);
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemShortParameter(length, buf)
                          .AddTimeoutParameter(timeout)
                          .AddLockCookieParameter(lockCookie);

            return cmd;
        }

        protected virtual SqlCommand CreateUpdateStateItemShortNullLongCmd(string id, byte[] buf, int length, int timeout, int lockCookie)
        {
            var cmd = CreateSqlCommand(UpdateStateItemShortNullLongSql);
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemShortParameter(length, buf)
                          .AddTimeoutParameter(timeout)
                          .AddLockCookieParameter(lockCookie);

            return cmd;
        }

        protected virtual SqlCommand CreateUpdateStateItemLongNullShortCmd(string id, byte[] buf, int length, int timeout, int lockCookie)
        {
            var cmd = CreateSqlCommand(UpdateStateItemLongNullShortSql);
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemLongParameter(length, buf)
                          .AddTimeoutParameter(timeout)
                          .AddLockCookieParameter(lockCookie);

            return cmd;
        }

        protected virtual SqlCommand CreateUpdateStateItemLongCmd(string id, byte[] buf, int length, int timeout, int lockCookie)
        {
            var cmd = CreateSqlCommand(UpdateStateItemLongSql);
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemLongParameter(length, buf)
                          .AddTimeoutParameter(timeout)
                          .AddLockCookieParameter(lockCookie);

            return cmd;
        }

        protected virtual SqlCommand CreateInsertStateItemShortCmd(string id, byte[] buf, int length, int timeout)
        {
            var cmd = CreateSqlCommand(InsertStateItemShortSql);
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemShortParameter(length, buf)
                          .AddTimeoutParameter(timeout);

            return cmd;
        }

        protected virtual SqlCommand CreateInsertStateItemLongCmd(string id, byte[] buf, int length, int timeout)
        {
            var cmd = CreateSqlCommand(InsertStateItemLongSql);
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemLongParameter(length, buf)
                          .AddTimeoutParameter(timeout);

            return cmd;
        }

        protected virtual SqlCommand CreateDeleteExpiredSessionsCmd()
        {
            return CreateSqlCommand(DeleteExpiredSessionsSql);
        }

        protected SqlCommand CreateSqlCommand(string sql)
        {
            return new SqlCommand()
            {
                CommandType = CommandType.Text,
                CommandTimeout = commandTimeout,
                CommandText = sql
            };
        }

        //protected virtual Dictionary<string, string> CreateSqlStatementDictionary()
        //{
        //    var sqlStatementDictionary = new Dictionary<string, string>();
        //    // SQL server version must be >= 8.0
        //    #region CreateSessionTable
        //    sqlStatementDictionary["CreateSessionTable"] = $@"
        //       IF NOT EXISTS (SELECT * 
        //         FROM INFORMATION_SCHEMA.TABLES 
        //         WHERE TABLE_NAME = '{SqlCommandUtil.TableName}')
        //       BEGIN
        //        CREATE TABLE {SqlCommandUtil.TableName} (
        //        SessionId           nvarchar(88)    NOT NULL PRIMARY KEY,
        //        Created             datetime        NOT NULL DEFAULT GETUTCDATE(),
        //        Expires             datetime        NOT NULL,
        //        LockDate            datetime        NOT NULL,
        //        LockDateLocal       datetime        NOT NULL,
        //        LockCookie          int             NOT NULL,
        //        Timeout             int             NOT NULL,
        //        Locked              bit             NOT NULL,
        //        SessionItemShort    varbinary({SqlCommandUtil.ItemShortLength}) NULL,
        //        SessionItemLong     image           NULL,
        //        Flags               int             NOT NULL DEFAULT 0,
        //        ) 
        //        CREATE NONCLUSTERED INDEX Index_Expires ON {SqlCommandUtil.TableName} (Expires)
        //    END";
        //    #endregion

        //    #region TempInsertUninitializedItem
        //    sqlStatementDictionary["TempInsertUninitializedItem"] = $@"
        //    DECLARE @now AS datetime
        //    DECLARE @nowLocal AS datetime
        //    SET @now = GETUTCDATE()
        //    SET @nowLocal = GETDATE()

        //    INSERT {SqlCommandUtil.TableName} (SessionId, 
        //         SessionItemShort, 
        //         Timeout, 
        //         Expires, 
        //         Locked, 
        //         LockDate,
        //         LockDateLocal,
        //         LockCookie,
        //         Flags) 
        //    VALUES
        //        (@{SqlParameterName.SessionId},
        //         @{SqlParameterName.SessionItemShort},
        //         @{SqlParameterName.Timeout},
        //         DATEADD(n, @{SqlParameterName.Timeout}, @now),
        //         0,
        //         @now,
        //         @nowLocal,
        //         1,
        //         1)";
        //    #endregion

        //    #region GetStateItemExclusive
        //    sqlStatementDictionary["GetStateItemExclusive"] = $@"
        //    DECLARE @textptr AS varbinary(16)
        //    DECLARE @length AS int
        //    DECLARE @now AS datetime
        //    DECLARE @nowLocal AS datetime
            
        //    SET @now = GETUTCDATE()
        //    SET @nowLocal = GETDATE()
            
        //    UPDATE {SqlCommandUtil.TableName}
        //    SET Expires = DATEADD(n, Timeout, @now), 
        //        LockDate = CASE Locked
        //            WHEN 0 THEN @now
        //            ELSE LockDate
        //            END,
        //        LockDateLocal = CASE Locked
        //            WHEN 0 THEN @nowLocal
        //            ELSE LockDateLocal
        //            END,
        //        @{SqlParameterName.LockAge} = CASE Locked
        //            WHEN 0 THEN 0
        //            ELSE DATEDIFF(second, LockDate, @now)
        //            END,
        //        @{SqlParameterName.LockCookie} = LockCookie = CASE Locked
        //            WHEN 0 THEN LockCookie + 1
        //            ELSE LockCookie
        //            END,
        //        @{SqlParameterName.SessionItemShort} = CASE Locked
        //            WHEN 0 THEN SessionItemShort
        //            ELSE NULL
        //            END,
        //        @textptr = CASE Locked
        //            WHEN 0 THEN TEXTPTR(SessionItemLong)
        //            ELSE NULL
        //            END,
        //        @length = CASE Locked
        //            WHEN 0 THEN DATALENGTH(SessionItemLong)
        //            ELSE NULL
        //            END,
        //        @{SqlParameterName.Locked} = Locked,
        //        Locked = 1,

        //        /* If the Uninitialized flag (0x1) if it is set,
        //           remove it and return InitializeItem (0x1) in actionFlags */
        //        Flags = CASE
        //            WHEN (Flags & 1) <> 0 THEN (Flags & ~1)
        //            ELSE Flags
        //            END,
        //        @{SqlParameterName.ActionFlags} = CASE
        //            WHEN (Flags & 1) <> 0 THEN 1
        //            ELSE 0
        //            END
        //    WHERE SessionId = @{SqlParameterName.SessionId}
        //    IF @length IS NOT NULL BEGIN
        //        READTEXT {SqlCommandUtil.TableName}.SessionItemLong @textptr 0 @length
        //    END";
        //    #endregion

        //    #region GetStateItem
        //    sqlStatementDictionary["GetStateItem"] = $@"
        //    DECLARE @textptr AS varbinary(16)
        //    DECLARE @length AS int
        //    DECLARE @now AS datetime
        //    SET @now = GETUTCDATE()

        //    UPDATE {SqlCommandUtil.TableName}
        //    SET Expires = DATEADD(n, Timeout, @now), 
        //        @{SqlParameterName.Locked} = Locked,
        //        @{SqlParameterName.LockAge} = DATEDIFF(second, LockDate, @now),
        //        @{SqlParameterName.LockCookie} = LockCookie,
        //        @{SqlParameterName.SessionItemShort} = CASE @locked
        //            WHEN 0 THEN SessionItemShort
        //            ELSE NULL
        //            END,
        //        @textptr = CASE @locked
        //            WHEN 0 THEN TEXTPTR(SessionItemLong)
        //            ELSE NULL
        //            END,
        //        @length = CASE @locked
        //            WHEN 0 THEN DATALENGTH(SessionItemLong)
        //            ELSE NULL
        //            END,

        //        /* If the Uninitialized flag (0x1) if it is set,
        //           remove it and return InitializeItem (0x1) in actionFlags */
        //        Flags = CASE
        //            WHEN (Flags & 1) <> 0 THEN (Flags & ~1)
        //            ELSE Flags
        //            END,
        //        @{SqlParameterName.ActionFlags} = CASE
        //            WHEN (Flags & 1) <> 0 THEN 1
        //            ELSE 0
        //            END
        //    WHERE SessionId = @{SqlParameterName.SessionId}
        //    IF @length IS NOT NULL BEGIN
        //        READTEXT {SqlCommandUtil.TableName}.SessionItemLong @textptr 0 @length
        //    END";
        //    #endregion

        //    #region ReleaseItemExclusive
        //    sqlStatementDictionary["ReleaseItemExclusive"] = $@"
        //    UPDATE {SqlCommandUtil.TableName}
        //    SET Expires = DATEADD(n, Timeout, GETDATE()),
        //        Locked = 0
        //    WHERE SessionId = @{SqlParameterName.SessionId} AND LockCookie = @{SqlParameterName.LockCookie}";
        //    #endregion

        //    #region RemoveStateItem
        //    sqlStatementDictionary["RemoveStateItem"] = $@"
        //    DELETE {SqlCommandUtil.TableName}
        //    WHERE SessionId = @{SqlParameterName.SessionId} AND LockCookie = @{SqlParameterName.LockCookie}";
        //    #endregion

        //    #region ResetItemTimeout
        //    sqlStatementDictionary["ResetItemTimeout"] = $@"
        //    UPDATE {SqlCommandUtil.TableName}
        //    SET Expires = DATEADD(n, Timeout, GETUTCDATE())
        //    WHERE SessionId = @{SqlParameterName.SessionId}";
        //    #endregion

        //    #region UpdateStateItemShort
        //    sqlStatementDictionary["UpdateStateItemShort"] = $@"
        //    UPDATE {SqlCommandUtil.TableName}
        //    SET Expires = DATEADD(n, @{SqlParameterName.Timeout}, GETUTCDATE()), 
        //        SessionItemShort = @{SqlParameterName.SessionItemShort}, 
        //        Timeout = @{SqlParameterName.Timeout},
        //        Locked = 0
        //    WHERE SessionId = @{SqlParameterName.SessionId} AND LockCookie = @{SqlParameterName.LockCookie}";
        //    #endregion

        //    #region UpdateStateItemShortNullLong
        //    sqlStatementDictionary["UpdateStateItemShortNullLong"] = $@"
        //    UPDATE {SqlCommandUtil.TableName}
        //    SET Expires = DATEADD(n, @{SqlParameterName.Timeout}, GETDATE()), 
        //        SessionItemShort = @{SqlParameterName.SessionItemShort}, 
        //        SessionItemLong = NULL, 
        //        Timeout = @{SqlParameterName.Timeout},
        //        Locked = 0
        //    WHERE SessionId = @{SqlParameterName.SessionId} AND LockCookie = @{SqlParameterName.LockCookie}";
        //    #endregion

        //    #region UpdateStateItemLongNullShort
        //    sqlStatementDictionary["UpdateStateItemLongNullShort"] = $@"
        //    UPDATE {SqlCommandUtil.TableName}
        //    SET Expires = DATEADD(n, @{SqlParameterName.Timeout}, GETUTCDATE()), 
        //        SessionItemLong = @{SqlParameterName.SessionItemLong}, 
        //        SessionItemShort = NULL,
        //        Timeout = @{SqlParameterName.Timeout},
        //        Locked = 0
        //    WHERE SessionId = @{SqlParameterName.SessionId} AND LockCookie = @{SqlParameterName.LockCookie}";
        //    #endregion

        //    #region UpdateStateItemLong
        //    sqlStatementDictionary["UpdateStateItemLong"] = $@"
        //    UPDATE {SqlCommandUtil.TableName}
        //    SET Expires = DATEADD(n, @{SqlParameterName.Timeout}, GETUTCDATE()), 
        //        SessionItemLong = @{SqlParameterName.SessionItemLong},
        //        Timeout = @{SqlParameterName.Timeout},
        //        Locked = 0
        //    WHERE SessionId = @{SqlParameterName.SessionId} AND LockCookie = @{SqlParameterName.LockCookie}";
        //    #endregion

        //    #region InsertStateItemShort
        //    sqlStatementDictionary["InsertStateItemShort"] = $@"
        //    DECLARE @now AS datetime
        //    DECLARE @nowLocal AS datetime
            
        //    SET @now = GETUTCDATE()
        //    SET @nowLocal = GETDATE()

        //    INSERT {SqlCommandUtil.TableName} 
        //        (SessionId, 
        //         SessionItemShort, 
        //         Timeout, 
        //         Expires, 
        //         Locked, 
        //         LockDate,
        //         LockDateLocal,
        //         LockCookie) 
        //    VALUES 
        //        (@{SqlParameterName.SessionId}, 
        //         @{SqlParameterName.SessionItemShort}, 
        //         @{SqlParameterName.Timeout}, 
        //         DATEADD(n, @{SqlParameterName.Timeout}, @now), 
        //         0, 
        //         @now,
        //         @nowLocal,
        //         1)";
        //    #endregion

        //    #region InsertStateItemLong
        //    sqlStatementDictionary["InsertStateItemLong"] = $@"
        //    DECLARE @now AS datetime
        //    DECLARE @nowLocal AS datetime
            
        //    SET @now = GETUTCDATE()
        //    SET @nowLocal = GETDATE()

        //    INSERT {SqlCommandUtil.TableName} 
        //        (SessionId, 
        //         SessionItemLong, 
        //         Timeout, 
        //         Expires, 
        //         Locked, 
        //         LockDate,
        //         LockDateLocal,
        //         LockCookie) 
        //    VALUES 
        //        (@{SqlParameterName.SessionId}, 
        //         @{SqlParameterName.SessionItemLong}, 
        //         @{SqlParameterName.Timeout}, 
        //         DATEADD(n, @{SqlParameterName.Timeout}, @now), 
        //         0, 
        //         @now,
        //         @nowLocal,
        //         1)";
        //    #endregion

        //    #region DeleteExpiredSessions
        //    sqlStatementDictionary["DeleteExpiredSessions"] = $@"
        //    SET NOCOUNT ON
        //    SET DEADLOCK_PRIORITY LOW

        //    DECLARE @now datetime
        //    SET @now = GETUTCDATE() 

        //    CREATE TABLE #tblExpiredSessions 
        //    ( 
        //        SessionId nvarchar({SqlCommandUtil.IdLength}) NOT NULL PRIMARY KEY
        //    )

        //    INSERT #tblExpiredSessions (SessionId)
        //        SELECT SessionId
        //        FROM {SqlCommandUtil.TableName} WITH (READUNCOMMITTED)
        //        WHERE Expires < @now

        //    IF @@ROWCOUNT <> 0 
        //    BEGIN 
        //        DECLARE ExpiredSessionCursor CURSOR LOCAL FORWARD_ONLY READ_ONLY
        //        FOR SELECT SessionId FROM #tblExpiredSessions

        //        DECLARE @SessionId nvarchar({SqlCommandUtil.IdLength})

        //        OPEN ExpiredSessionCursor

        //        FETCH NEXT FROM ExpiredSessionCursor INTO @SessionId

        //        WHILE @@FETCH_STATUS = 0 
        //            BEGIN
        //                DELETE FROM {SqlCommandUtil.TableName} WHERE SessionId = @SessionId AND Expires < @now
        //                FETCH NEXT FROM ExpiredSessionCursor INTO @SessionId
        //            END

        //        CLOSE ExpiredSessionCursor

        //        DEALLOCATE ExpiredSessionCursor
        //    END 

        //    DROP TABLE #tblExpiredSessions";
        //    #endregion

        //    return sqlStatementDictionary;
        //}

        
    }    
}