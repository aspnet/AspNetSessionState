namespace Microsoft.AspNet.SessionState
{
    using System.Data.SqlClient;

    class SqlInMemoryTableSessionStateRepository : SqlSessionStateRepository
    {
        #region Sql statement
        // Premium database on a V12 server is required for InMemoryTable
        // DB owner needs to ALTER DATABASE [Database Name] SET MEMORY_OPTIMIZED_ELEVATE_TO_SNAPSHOT=ON;
        // Most of the SQL statement should just work, the following statements are different
        #region CreateSessionTable
        private static readonly string CreateSessionTableSql = $@"
               IF NOT EXISTS (SELECT * 
                 FROM INFORMATION_SCHEMA.TABLES 
                 WHERE TABLE_NAME = '{SqlSessionStateRepositoryUtil.TableName}')
               BEGIN
                CREATE TABLE {SqlSessionStateRepositoryUtil.TableName} (
                SessionId           nvarchar(88)    COLLATE Latin1_General_100_BIN2 NOT NULL,
                Created             datetime        NOT NULL DEFAULT GETUTCDATE(),
                Expires             datetime        NOT NULL,
                LockDate            datetime        NOT NULL,
                LockDateLocal       datetime        NOT NULL,
                LockCookie          int             NOT NULL,
                Timeout             int             NOT NULL,
                Locked              bit             NOT NULL,
                SessionItemShort    varbinary({SqlSessionStateRepositoryUtil.ItemShortLength}) NULL,
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
        private static readonly string GetStateItemExclusiveSql = $@"
                DECLARE @textptr AS varbinary(max)
                DECLARE @length AS int
                DECLARE @now AS datetime
                DECLARE @nowLocal AS datetime

                SET @now = GETUTCDATE()
                SET @nowLocal = GETDATE()

                DECLARE @LockedCheck bit
                DECLARE @Flags int

                SELECT @LockedCheck = Locked, @Flags = Flags FROM {SqlSessionStateRepositoryUtil.TableName} WHERE SessionID = @{SqlParameterName.SessionId}
                IF @Flags&1 <> 0
                BEGIN
                    SET @actionFlags = 1
                    UPDATE {SqlSessionStateRepositoryUtil.TableName} SET Flags = Flags & ~1 WHERE SessionID = @{SqlParameterName.SessionId}
                END
                ELSE
                    SET @{SqlParameterName.ActionFlags} = 0

                IF @LockedCheck = 1
                BEGIN
                    UPDATE {SqlSessionStateRepositoryUtil.TableName}
                    SET Expires = DATEADD(n, Timeout, @now), 
                        @{SqlParameterName.LockAge} = DATEDIFF(second, LockDate, @now),
                        @{SqlParameterName.LockCookie} = LockCookie,
                        @{SqlParameterName.SessionItemShort} = NULL,
                        --@textptr = NULL,
                        @length = NULL,
                        @{SqlParameterName.Locked} = 1
                    WHERE SessionId = @{SqlParameterName.SessionId}
                END
                ELSE
                BEGIN
                    UPDATE {SqlSessionStateRepositoryUtil.TableName}
                    SET Expires = DATEADD(n, Timeout, @now), 
                        LockDate = @now,
                        LockDateLocal = @nowlocal,
                        @{SqlParameterName.LockAge} = 0,
                        @{SqlParameterName.LockCookie} = LockCookie = LockCookie + 1,
                        @{SqlParameterName.SessionItemShort} = SessionItemShort,
                        @textptr = SessionItemLong,
                        @length = 1,
                        @{SqlParameterName.Locked} = 0,
                        Locked = 1
                    WHERE SessionId = @{SqlParameterName.SessionId}

                    IF @TextPtr IS NOT NULL
                        SELECT @TextPtr
                END";
        #endregion

        #region GetStateItem            
        private static readonly string GetStateItemSql = $@"
                DECLARE @textptr AS varbinary(max)
                DECLARE @length AS int
                DECLARE @now AS datetime
                SET @now = GETUTCDATE()

                UPDATE {SqlSessionStateRepositoryUtil.TableName}
                SET Expires = DATEADD(n, Timeout, @now), 
                    @{SqlParameterName.Locked} = Locked,
                    @{SqlParameterName.LockAge} = DATEDIFF(second, LockDate, @now),
                    @{SqlParameterName.LockCookie} = LockCookie,
                    @{SqlParameterName.SessionItemShort} = CASE @{SqlParameterName.Locked}
                        WHEN 0 THEN SessionItemShort
                        ELSE NULL
                        END,
                    @textptr = CASE @{SqlParameterName.Locked}
                        WHEN 0 THEN SessionItemLong
                        ELSE NULL
                        END,
                    @length = CASE @{SqlParameterName.Locked}
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
                    SELECT @textptr
                END
            ";
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
                    FROM {SqlSessionStateRepositoryUtil.TableName} WITH (SNAPSHOT)
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

        public SqlInMemoryTableSessionStateRepository(int commandTimeout) : base(commandTimeout) { }

        protected override SqlCommand CreateCreateSessionTableCmd()
        {
            return CreateSqlCommand(CreateSessionTableSql);
        }

        protected override SqlCommand CreateGetStateItemExclusiveCmd(string id)
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

        protected override SqlCommand CreateGetStateItemCmd(string id)
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

        protected override SqlCommand CreateDeleteExpiredSessionsCmd()
        {
            return CreateSqlCommand(DeleteExpiredSessionsSql);
        }
    }
}
