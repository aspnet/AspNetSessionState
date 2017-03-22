using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.AspNet.SessionState
{
    internal interface ISqlStateCommandCreator
    {
        SqlCommand CreateCreateSessionTableCmd();

        SqlCommand CreateTempInsertUninitializedItemCmd(string id, int length, byte[] buf, int timeout);

        SqlCommand CreateGetStateItemExclusiveCmd(string id);

        SqlCommand CreateGetStateItemCmd(string id);

        SqlCommand CreateReleaseItemExclusiveCmd(string id, object lockid);

        SqlCommand CreateRemoveStateItemCmd(string id, object lockid);

        SqlCommand CreateResetItemTimeoutCmd(string id);

        SqlCommand CreateUpdateStateItemShortCmd(string id, byte[] buf, int length, int timeout, int lockCookie);

        SqlCommand CreateUpdateStateItemShortNullLongCmd(string id, byte[] buf, int length, int timeout, int lockCookie);

        SqlCommand CreateUpdateStateItemLongNullShortCmd(string id, byte[] buf, int length, int timeout, int lockCookie);

        SqlCommand CreateUpdateStateItemLongCmd(string id, byte[] buf, int length, int timeout, int lockCookie);

        SqlCommand CreateInsertStateItemShortCmd(string id, byte[] buf, int length, int timeout);

        SqlCommand CreateInsertStateItemLongCmd(string id, byte[] buf, int length, int timeout);

        SqlCommand CreateDeleteExpiredSessionsCmd();
    }

    /// <summary>
    /// SQL server version must be >= 8.0
    /// </summary>
    internal class SqlStateCommandCreator : ISqlStateCommandCreator
    {
        private static bool s_initialized;
        private static int s_commandTimeout;
        protected static Dictionary<string, string> s_sqlStatementDictionary = new Dictionary<string, string>();

        public SqlStateCommandCreator(int commandTimeout)
        {
            if (!s_initialized)
            {
                InitializeSqlStatementDictionary();
                s_commandTimeout = commandTimeout;
                s_initialized = true;
            }
        }

        public SqlCommand CreateCreateSessionTableCmd()
        {
            var cmd = CreateSqlTextCmd("CreateSessionTable");

            return cmd;
        }

        public SqlCommand CreateTempInsertUninitializedItemCmd(string id, int length, byte[] buf, int timeout)
        {
            var cmd = CreateSqlTextCmd("TempInsertUninitializedItem");
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemShortParameter(length, buf)
                          .AddTimeoutParameter(timeout);

            return cmd;
        }

        public SqlCommand CreateGetStateItemExclusiveCmd(string id)
        {
            // TODO: need to check if this sql statement works in in-memoryTable
            var cmd = CreateSqlTextCmd("GetStateItemExclusive");
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemShortParameter()
                          .AddLockAgeParameter()
                          .AddLockedParameter()
                          .AddLockCookieParameter()
                          .AddActionFlagsParameter();

            return cmd;
        }

        public SqlCommand CreateGetStateItemCmd(string id)
        {
            var cmd = CreateSqlTextCmd("GetStateItem");
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemShortParameter()
                          .AddLockedParameter()
                          .AddLockAgeParameter()
                          .AddLockCookieParameter()
                          .AddActionFlagsParameter();

            return cmd;
        }

        public SqlCommand CreateReleaseItemExclusiveCmd(string id, object lockid)
        {
            var cmd = CreateSqlTextCmd("ReleaseItemExclusive");
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddLockCookieParameter(lockid);

            return cmd;
        }

        public SqlCommand CreateRemoveStateItemCmd(string id, object lockid)
        {
            var cmd = CreateSqlTextCmd("RemoveStateItem");
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddLockCookieParameter(lockid);

            return cmd;
        }

        public SqlCommand CreateResetItemTimeoutCmd(string id)
        {
            var cmd = CreateSqlTextCmd("ResetItemTimeout");
            cmd.Parameters.AddSessionIdParameter(id);

            return cmd;
        }

        public  SqlCommand CreateUpdateStateItemShortCmd(string id, byte[] buf, int length, int timeout, int lockCookie)
        {
            var cmd = CreateSqlTextCmd("UpdateStateItemShort");
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemShortParameter(length, buf)
                          .AddTimeoutParameter(timeout)
                          .AddLockCookieParameter(lockCookie);

            return cmd;
        }

        public SqlCommand CreateUpdateStateItemShortNullLongCmd(string id, byte[] buf, int length, int timeout, int lockCookie)
        {
            var cmd = CreateSqlTextCmd("UpdateStateItemShortNullLong");
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemShortParameter(length, buf)
                          .AddTimeoutParameter(timeout)
                          .AddLockCookieParameter(lockCookie);

            return cmd;
        }

        public SqlCommand CreateUpdateStateItemLongNullShortCmd(string id, byte[] buf, int length, int timeout, int lockCookie)
        {
            var cmd = CreateSqlTextCmd("UpdateStateItemLongNullShort");
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemLongParameter(length, buf)
                          .AddTimeoutParameter(timeout)
                          .AddLockCookieParameter(lockCookie);

            return cmd;
        }

        public SqlCommand CreateUpdateStateItemLongCmd(string id, byte[] buf, int length, int timeout, int lockCookie)
        {
            var cmd = CreateSqlTextCmd("UpdateStateItemLong");
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemLongParameter(length, buf)
                          .AddTimeoutParameter(timeout)
                          .AddLockCookieParameter(lockCookie);

            return cmd;
        }

        public SqlCommand CreateInsertStateItemShortCmd(string id, byte[] buf, int length, int timeout)
        {
            var cmd = CreateSqlTextCmd("InsertStateItemShort");
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemShortParameter(length, buf)
                          .AddTimeoutParameter(timeout);

            return cmd;
        }

        public SqlCommand CreateInsertStateItemLongCmd(string id, byte[] buf, int length, int timeout)
        {
            var cmd = CreateSqlTextCmd("InsertStateItemLong");
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemLongParameter(length, buf)
                          .AddTimeoutParameter(timeout);

            return cmd;
        }

        public SqlCommand CreateDeleteExpiredSessionsCmd()
        {
            return CreateSqlTextCmd("DeleteExpiredSessions");
        }

        private SqlCommand CreateSqlTextCmd(string statementName)
        {
            // TODO:
            if (!s_initialized)
            {
                throw new Exception("Call SqlStateCommandFactory.Initialize first");
            }

            return new SqlCommand()
            {
                CommandType = CommandType.Text,
                CommandTimeout = s_commandTimeout,
                CommandText = GetSqlStatement(statementName)
            };
        }

        protected string GetSqlStatement(string statementName)
        {
            Debug.Assert(s_sqlStatementDictionary.ContainsKey(statementName));

            return s_sqlStatementDictionary[statementName];
        }

        private static void InitializeSqlStatementDictionary()
        {
            // SQL server version must be >= 8.0
            #region CreateSessionTable
            s_sqlStatementDictionary["CreateSessionTable"] = $@"
               IF NOT EXISTS (SELECT * 
                 FROM INFORMATION_SCHEMA.TABLES 
                 WHERE TABLE_NAME = '{SqlStateCommandUtil.TableName}')
               BEGIN
                CREATE TABLE {SqlStateCommandUtil.TableName} (
                SessionId           nvarchar(88)    NOT NULL PRIMARY KEY,
                Created             datetime        NOT NULL DEFAULT GETUTCDATE(),
                Expires             datetime        NOT NULL,
                LockDate            datetime        NOT NULL,
                LockDateLocal       datetime        NOT NULL,
                LockCookie          int             NOT NULL,
                Timeout             int             NOT NULL,
                Locked              bit             NOT NULL,
                SessionItemShort    varbinary({SqlStateCommandUtil.ItemShortLength}) NULL,
                SessionItemLong     image           NULL,
                Flags               int             NOT NULL DEFAULT 0,
                ) 
                CREATE NONCLUSTERED INDEX Index_Expires ON {SqlStateCommandUtil.TableName} (Expires)
            END";
            #endregion

            #region TempInsertUninitializedItem
            s_sqlStatementDictionary["TempInsertUninitializedItem"] = $@"
            DECLARE @now AS datetime
            DECLARE @nowLocal AS datetime
            SET @now = GETUTCDATE()
            SET @nowLocal = GETDATE()

            INSERT {SqlStateCommandUtil.TableName} (SessionId, 
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
            s_sqlStatementDictionary["GetStateItemExclusive"] = $@"
            DECLARE @textptr AS varbinary(16)
            DECLARE @length AS int
            DECLARE @now AS datetime
            DECLARE @nowLocal AS datetime
            
            SET @now = GETUTCDATE()
            SET @nowLocal = GETDATE()
            
            UPDATE {SqlStateCommandUtil.TableName}
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
                READTEXT {SqlStateCommandUtil.TableName}.SessionItemLong @textptr 0 @length
            END";
            #endregion

            #region GetStateItem
            s_sqlStatementDictionary["GetStateItem"] = $@"
            DECLARE @textptr AS varbinary(16)
            DECLARE @length AS int
            DECLARE @now AS datetime
            SET @now = GETUTCDATE()

            UPDATE {SqlStateCommandUtil.TableName}
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
                READTEXT {SqlStateCommandUtil.TableName}.SessionItemLong @textptr 0 @length
            END";
            #endregion

            #region ReleaseItemExclusive
            s_sqlStatementDictionary["ReleaseItemExclusive"] = $@"
            UPDATE {SqlStateCommandUtil.TableName}
            SET Expires = DATEADD(n, Timeout, GETDATE()),
                Locked = 0
            WHERE SessionId = @{SqlParameterName.SessionId} AND LockCookie = @{SqlParameterName.LockCookie}";
            #endregion

            #region RemoveStateItem
            s_sqlStatementDictionary["RemoveStateItem"] = $@"
            DELETE {SqlStateCommandUtil.TableName}
            WHERE SessionId = @{SqlParameterName.SessionId} AND LockCookie = @{SqlParameterName.LockCookie}";
            #endregion

            #region ResetItemTimeout
            s_sqlStatementDictionary["ResetItemTimeout"] = $@"
            UPDATE {SqlStateCommandUtil.TableName}
            SET Expires = DATEADD(n, Timeout, GETUTCDATE())
            WHERE SessionId = @{SqlParameterName.SessionId}";
            #endregion

            #region UpdateStateItemShort
            s_sqlStatementDictionary["UpdateStateItemShort"] = $@"
            UPDATE {SqlStateCommandUtil.TableName}
            SET Expires = DATEADD(n, @{SqlParameterName.Timeout}, GETUTCDATE()), 
                SessionItemShort = @{SqlParameterName.SessionItemShort}, 
                Timeout = @{SqlParameterName.Timeout},
                Locked = 0
            WHERE SessionId = @{SqlParameterName.SessionId} AND LockCookie = @{SqlParameterName.LockCookie}";
            #endregion

            #region UpdateStateItemShortNullLong
            s_sqlStatementDictionary["UpdateStateItemShortNullLong"] = $@"
            UPDATE {SqlStateCommandUtil.TableName}
            SET Expires = DATEADD(n, @{SqlParameterName.Timeout}, GETDATE()), 
                SessionItemShort = @{SqlParameterName.SessionItemShort}, 
                SessionItemLong = NULL, 
                Timeout = @{SqlParameterName.Timeout},
                Locked = 0
            WHERE SessionId = @{SqlParameterName.SessionId} AND LockCookie = @{SqlParameterName.LockCookie}";
            #endregion

            #region UpdateStateItemLongNullShort
            s_sqlStatementDictionary["UpdateStateItemLongNullShort"] = $@"
            UPDATE {SqlStateCommandUtil.TableName}
            SET Expires = DATEADD(n, @{SqlParameterName.Timeout}, GETUTCDATE()), 
                SessionItemLong = @{SqlParameterName.SessionItemLong}, 
                SessionItemShort = NULL,
                Timeout = @{SqlParameterName.Timeout},
                Locked = 0
            WHERE SessionId = @{SqlParameterName.SessionId} AND LockCookie = @{SqlParameterName.LockCookie}";
            #endregion

            #region UpdateStateItemLong
            s_sqlStatementDictionary["UpdateStateItemLong"] = $@"
            UPDATE {s_commandTimeout}
            SET Expires = DATEADD(n, @{SqlParameterName.Timeout}, GETUTCDATE()), 
                SessionItemLong = @{SqlParameterName.SessionItemLong},
                Timeout = @{SqlParameterName.Timeout},
                Locked = 0
            WHERE SessionId = @{SqlParameterName.SessionId} AND LockCookie = @{SqlParameterName.LockCookie}";
            #endregion

            #region InsertStateItemShort
            s_sqlStatementDictionary["InsertStateItemShort"] = $@"
            DECLARE @now AS datetime
            DECLARE @nowLocal AS datetime
            
            SET @now = GETUTCDATE()
            SET @nowLocal = GETDATE()

            INSERT {SqlStateCommandUtil.TableName} 
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
            s_sqlStatementDictionary["InsertStateItemLong"] = $@"
            DECLARE @now AS datetime
            DECLARE @nowLocal AS datetime
            
            SET @now = GETUTCDATE()
            SET @nowLocal = GETDATE()

            INSERT {SqlStateCommandUtil.TableName} 
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
            s_sqlStatementDictionary["DeleteExpiredSessions"] = $@"
            SET NOCOUNT ON
            SET DEADLOCK_PRIORITY LOW

            DECLARE @now datetime
            SET @now = GETUTCDATE() 

            CREATE TABLE #tblExpiredSessions 
            ( 
                SessionId nvarchar({SqlStateCommandUtil.IdLength}) NOT NULL PRIMARY KEY
            )

            INSERT #tblExpiredSessions (SessionId)
                SELECT SessionId
                FROM {SqlStateCommandUtil.TableName} WITH (READUNCOMMITTED)
                WHERE Expires < @now

            IF @@ROWCOUNT <> 0 
            BEGIN 
                DECLARE ExpiredSessionCursor CURSOR LOCAL FORWARD_ONLY READ_ONLY
                FOR SELECT SessionId FROM #tblExpiredSessions

                DECLARE @SessionId nvarchar({SqlStateCommandUtil.IdLength})

                OPEN ExpiredSessionCursor

                FETCH NEXT FROM ExpiredSessionCursor INTO @SessionId

                WHILE @@FETCH_STATUS = 0 
                    BEGIN
                        DELETE FROM {SqlStateCommandUtil.TableName} WHERE SessionId = @SessionId AND Expires < @now
                        FETCH NEXT FROM ExpiredSessionCursor INTO @SessionId
                    END

                CLOSE ExpiredSessionCursor

                DEALLOCATE ExpiredSessionCursor
            END 

            DROP TABLE #tblExpiredSessions";
            #endregion
        }
    }    
}