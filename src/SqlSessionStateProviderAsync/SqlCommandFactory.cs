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
    internal interface ISqlCommandFactory
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
    internal class SqlCommandFactory : ISqlCommandFactory
    {
        private static bool s_initialized;
        private static int s_commandTimeout;
        private static Dictionary<string, string> s_sqlStatementDictionary = new Dictionary<string, string>();

        public SqlCommandFactory(int commandTimeout)
        {
            if (!s_initialized)
            {
                s_sqlStatementDictionary = CreateSqlStatementDictionary();
                s_commandTimeout = commandTimeout;
                s_initialized = true;
            }
        }

        public SqlCommand CreateCreateSessionTableCmd()
        {
            var cmd = CreateSqlCommand("CreateSessionTable");

            return cmd;
        }

        public SqlCommand CreateTempInsertUninitializedItemCmd(string id, int length, byte[] buf, int timeout)
        {
            var cmd = CreateSqlCommand("TempInsertUninitializedItem");
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemShortParameter(length, buf)
                          .AddTimeoutParameter(timeout);

            return cmd;
        }

        public SqlCommand CreateGetStateItemExclusiveCmd(string id)
        {
            // TODO: need to check if this sql statement works in in-memoryTable
            var cmd = CreateSqlCommand("GetStateItemExclusive");
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
            var cmd = CreateSqlCommand("GetStateItem");
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
            var cmd = CreateSqlCommand("ReleaseItemExclusive");
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddLockCookieParameter(lockid);

            return cmd;
        }

        public SqlCommand CreateRemoveStateItemCmd(string id, object lockid)
        {
            var cmd = CreateSqlCommand("RemoveStateItem");
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddLockCookieParameter(lockid);

            return cmd;
        }

        public SqlCommand CreateResetItemTimeoutCmd(string id)
        {
            var cmd = CreateSqlCommand("ResetItemTimeout");
            cmd.Parameters.AddSessionIdParameter(id);

            return cmd;
        }

        public  SqlCommand CreateUpdateStateItemShortCmd(string id, byte[] buf, int length, int timeout, int lockCookie)
        {
            var cmd = CreateSqlCommand("UpdateStateItemShort");
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemShortParameter(length, buf)
                          .AddTimeoutParameter(timeout)
                          .AddLockCookieParameter(lockCookie);

            return cmd;
        }

        public SqlCommand CreateUpdateStateItemShortNullLongCmd(string id, byte[] buf, int length, int timeout, int lockCookie)
        {
            var cmd = CreateSqlCommand("UpdateStateItemShortNullLong");
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemShortParameter(length, buf)
                          .AddTimeoutParameter(timeout)
                          .AddLockCookieParameter(lockCookie);

            return cmd;
        }

        public SqlCommand CreateUpdateStateItemLongNullShortCmd(string id, byte[] buf, int length, int timeout, int lockCookie)
        {
            var cmd = CreateSqlCommand("UpdateStateItemLongNullShort");
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemLongParameter(length, buf)
                          .AddTimeoutParameter(timeout)
                          .AddLockCookieParameter(lockCookie);

            return cmd;
        }

        public SqlCommand CreateUpdateStateItemLongCmd(string id, byte[] buf, int length, int timeout, int lockCookie)
        {
            var cmd = CreateSqlCommand("UpdateStateItemLong");
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemLongParameter(length, buf)
                          .AddTimeoutParameter(timeout)
                          .AddLockCookieParameter(lockCookie);

            return cmd;
        }

        public SqlCommand CreateInsertStateItemShortCmd(string id, byte[] buf, int length, int timeout)
        {
            var cmd = CreateSqlCommand("InsertStateItemShort");
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemShortParameter(length, buf)
                          .AddTimeoutParameter(timeout);

            return cmd;
        }

        public SqlCommand CreateInsertStateItemLongCmd(string id, byte[] buf, int length, int timeout)
        {
            var cmd = CreateSqlCommand("InsertStateItemLong");
            cmd.Parameters.AddSessionIdParameter(id)
                          .AddSessionItemLongParameter(length, buf)
                          .AddTimeoutParameter(timeout);

            return cmd;
        }

        public SqlCommand CreateDeleteExpiredSessionsCmd()
        {
            return CreateSqlCommand("DeleteExpiredSessions");
        }

        private SqlCommand CreateSqlCommand(string statementName)
        {
            Debug.Assert(s_initialized);

            return new SqlCommand()
            {
                CommandType = CommandType.Text,
                CommandTimeout = s_commandTimeout,
                CommandText = GetSqlStatement(statementName)
            };
        }

        private string GetSqlStatement(string statementName)
        {
            Debug.Assert(s_sqlStatementDictionary.ContainsKey(statementName));

            return s_sqlStatementDictionary[statementName];
        }

        protected virtual Dictionary<string, string> CreateSqlStatementDictionary()
        {
            var sqlStatementDictionary = new Dictionary<string, string>();
            // SQL server version must be >= 8.0
            #region CreateSessionTable
            sqlStatementDictionary["CreateSessionTable"] = $@"
               IF NOT EXISTS (SELECT * 
                 FROM INFORMATION_SCHEMA.TABLES 
                 WHERE TABLE_NAME = '{SqlCommandUtil.TableName}')
               BEGIN
                CREATE TABLE {SqlCommandUtil.TableName} (
                SessionId           nvarchar(88)    NOT NULL PRIMARY KEY,
                Created             datetime        NOT NULL DEFAULT GETUTCDATE(),
                Expires             datetime        NOT NULL,
                LockDate            datetime        NOT NULL,
                LockDateLocal       datetime        NOT NULL,
                LockCookie          int             NOT NULL,
                Timeout             int             NOT NULL,
                Locked              bit             NOT NULL,
                SessionItemShort    varbinary({SqlCommandUtil.ItemShortLength}) NULL,
                SessionItemLong     image           NULL,
                Flags               int             NOT NULL DEFAULT 0,
                ) 
                CREATE NONCLUSTERED INDEX Index_Expires ON {SqlCommandUtil.TableName} (Expires)
            END";
            #endregion

            #region TempInsertUninitializedItem
            sqlStatementDictionary["TempInsertUninitializedItem"] = $@"
            DECLARE @now AS datetime
            DECLARE @nowLocal AS datetime
            SET @now = GETUTCDATE()
            SET @nowLocal = GETDATE()

            INSERT {SqlCommandUtil.TableName} (SessionId, 
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
            sqlStatementDictionary["GetStateItemExclusive"] = $@"
            DECLARE @textptr AS varbinary(16)
            DECLARE @length AS int
            DECLARE @now AS datetime
            DECLARE @nowLocal AS datetime
            
            SET @now = GETUTCDATE()
            SET @nowLocal = GETDATE()
            
            UPDATE {SqlCommandUtil.TableName}
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
                READTEXT {SqlCommandUtil.TableName}.SessionItemLong @textptr 0 @length
            END";
            #endregion

            #region GetStateItem
            sqlStatementDictionary["GetStateItem"] = $@"
            DECLARE @textptr AS varbinary(16)
            DECLARE @length AS int
            DECLARE @now AS datetime
            SET @now = GETUTCDATE()

            UPDATE {SqlCommandUtil.TableName}
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
                READTEXT {SqlCommandUtil.TableName}.SessionItemLong @textptr 0 @length
            END";
            #endregion

            #region ReleaseItemExclusive
            sqlStatementDictionary["ReleaseItemExclusive"] = $@"
            UPDATE {SqlCommandUtil.TableName}
            SET Expires = DATEADD(n, Timeout, GETDATE()),
                Locked = 0
            WHERE SessionId = @{SqlParameterName.SessionId} AND LockCookie = @{SqlParameterName.LockCookie}";
            #endregion

            #region RemoveStateItem
            sqlStatementDictionary["RemoveStateItem"] = $@"
            DELETE {SqlCommandUtil.TableName}
            WHERE SessionId = @{SqlParameterName.SessionId} AND LockCookie = @{SqlParameterName.LockCookie}";
            #endregion

            #region ResetItemTimeout
            sqlStatementDictionary["ResetItemTimeout"] = $@"
            UPDATE {SqlCommandUtil.TableName}
            SET Expires = DATEADD(n, Timeout, GETUTCDATE())
            WHERE SessionId = @{SqlParameterName.SessionId}";
            #endregion

            #region UpdateStateItemShort
            sqlStatementDictionary["UpdateStateItemShort"] = $@"
            UPDATE {SqlCommandUtil.TableName}
            SET Expires = DATEADD(n, @{SqlParameterName.Timeout}, GETUTCDATE()), 
                SessionItemShort = @{SqlParameterName.SessionItemShort}, 
                Timeout = @{SqlParameterName.Timeout},
                Locked = 0
            WHERE SessionId = @{SqlParameterName.SessionId} AND LockCookie = @{SqlParameterName.LockCookie}";
            #endregion

            #region UpdateStateItemShortNullLong
            sqlStatementDictionary["UpdateStateItemShortNullLong"] = $@"
            UPDATE {SqlCommandUtil.TableName}
            SET Expires = DATEADD(n, @{SqlParameterName.Timeout}, GETDATE()), 
                SessionItemShort = @{SqlParameterName.SessionItemShort}, 
                SessionItemLong = NULL, 
                Timeout = @{SqlParameterName.Timeout},
                Locked = 0
            WHERE SessionId = @{SqlParameterName.SessionId} AND LockCookie = @{SqlParameterName.LockCookie}";
            #endregion

            #region UpdateStateItemLongNullShort
            sqlStatementDictionary["UpdateStateItemLongNullShort"] = $@"
            UPDATE {SqlCommandUtil.TableName}
            SET Expires = DATEADD(n, @{SqlParameterName.Timeout}, GETUTCDATE()), 
                SessionItemLong = @{SqlParameterName.SessionItemLong}, 
                SessionItemShort = NULL,
                Timeout = @{SqlParameterName.Timeout},
                Locked = 0
            WHERE SessionId = @{SqlParameterName.SessionId} AND LockCookie = @{SqlParameterName.LockCookie}";
            #endregion

            #region UpdateStateItemLong
            sqlStatementDictionary["UpdateStateItemLong"] = $@"
            UPDATE {SqlCommandUtil.TableName}
            SET Expires = DATEADD(n, @{SqlParameterName.Timeout}, GETUTCDATE()), 
                SessionItemLong = @{SqlParameterName.SessionItemLong},
                Timeout = @{SqlParameterName.Timeout},
                Locked = 0
            WHERE SessionId = @{SqlParameterName.SessionId} AND LockCookie = @{SqlParameterName.LockCookie}";
            #endregion

            #region InsertStateItemShort
            sqlStatementDictionary["InsertStateItemShort"] = $@"
            DECLARE @now AS datetime
            DECLARE @nowLocal AS datetime
            
            SET @now = GETUTCDATE()
            SET @nowLocal = GETDATE()

            INSERT {SqlCommandUtil.TableName} 
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
            sqlStatementDictionary["InsertStateItemLong"] = $@"
            DECLARE @now AS datetime
            DECLARE @nowLocal AS datetime
            
            SET @now = GETUTCDATE()
            SET @nowLocal = GETDATE()

            INSERT {SqlCommandUtil.TableName} 
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
            sqlStatementDictionary["DeleteExpiredSessions"] = $@"
            SET NOCOUNT ON
            SET DEADLOCK_PRIORITY LOW

            DECLARE @now datetime
            SET @now = GETUTCDATE() 

            CREATE TABLE #tblExpiredSessions 
            ( 
                SessionId nvarchar({SqlCommandUtil.IdLength}) NOT NULL PRIMARY KEY
            )

            INSERT #tblExpiredSessions (SessionId)
                SELECT SessionId
                FROM {SqlCommandUtil.TableName} WITH (READUNCOMMITTED)
                WHERE Expires < @now

            IF @@ROWCOUNT <> 0 
            BEGIN 
                DECLARE ExpiredSessionCursor CURSOR LOCAL FORWARD_ONLY READ_ONLY
                FOR SELECT SessionId FROM #tblExpiredSessions

                DECLARE @SessionId nvarchar({SqlCommandUtil.IdLength})

                OPEN ExpiredSessionCursor

                FETCH NEXT FROM ExpiredSessionCursor INTO @SessionId

                WHILE @@FETCH_STATUS = 0 
                    BEGIN
                        DELETE FROM {SqlCommandUtil.TableName} WHERE SessionId = @SessionId AND Expires < @now
                        FETCH NEXT FROM ExpiredSessionCursor INTO @SessionId
                    END

                CLOSE ExpiredSessionCursor

                DEALLOCATE ExpiredSessionCursor
            END 

            DROP TABLE #tblExpiredSessions";
            #endregion

            return sqlStatementDictionary;
        }
    }    
}