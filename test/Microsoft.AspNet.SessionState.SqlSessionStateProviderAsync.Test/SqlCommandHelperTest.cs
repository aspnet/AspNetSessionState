// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See the License.txt file in the project root for full license information.

namespace Microsoft.AspNet.SessionState.SqlSessionStateAsyncProvider.Test
{
    using System;
    using System.Data;
    using System.Data.SqlClient;
    using Xunit;

    public class SqlCommandHelperTest
    {
        private const int SqlCommandTimeout = 10;
        private const string SessionId = "testid";
        private const string SqlStatement = "moq sql statement";
        private static readonly byte[] Buffer = new byte[BufferLength];
        private const int BufferLength = 123;
        private const int SessionTimeout = 120;
        private const int LockId = 1;

        [Fact]
        public void Constructor_Should_Initialize_CommandTimeout()
        {
            var helper = new SqlCommandHelper(SqlCommandTimeout);

            Assert.Equal(SqlCommandTimeout, helper.CommandTimeout);
        }

        [Fact]
        public void CreateNewSessionTableCmd_Should_Create_SqlCommand_Without_Parameters()
        {
            var helper = new SqlCommandHelper(SqlCommandTimeout);

            var cmd = helper.CreateNewSessionTableCmd(SqlStatement);

            VerifyBasicsOfSqlCommand(cmd);
            Assert.Empty(cmd.Parameters);
        }

        [Fact]
        public void CreateGetStateItemExclusiveCmd_Should_Create_SqlCommand_With_Right_Parameters()
        {
            var helper = new SqlCommandHelper(SqlCommandTimeout);

            var cmd = helper.CreateGetStateItemExclusiveCmd(SqlStatement, SessionId);

            VerifyBasicsOfSqlCommand(cmd);
            VerifySessionIdParameter(cmd);
            VerifyLockAgeParameter(cmd);
            VerifyLockedParameter(cmd);
            VerifyLockCookieParameter(cmd);
            VerifyActionFlagsParameter(cmd);
            Assert.Equal(5, cmd.Parameters.Count);
        }

        [Fact]
        public void CreateGetStateItemCmd_Should_Create_SqlCommand_With_Right_Parameters()
        {
            var helper = new SqlCommandHelper(SqlCommandTimeout);

            var cmd = helper.CreateGetStateItemCmd(SqlStatement, SessionId);

            VerifyBasicsOfSqlCommand(cmd);
            VerifySessionIdParameter(cmd);
            VerifyLockedParameter(cmd);
            VerifyLockAgeParameter(cmd);
            VerifyLockCookieParameter(cmd);
            VerifyActionFlagsParameter(cmd);
            Assert.Equal(5, cmd.Parameters.Count);
        }

        [Fact]
        public void CreateDeleteExpiredSessionsCmd_Should_Create_SqlCommand_Without_Parameters()
        {
            var helper = new SqlCommandHelper(SqlCommandTimeout);

            var cmd = helper.CreateDeleteExpiredSessionsCmd(SqlStatement);

            VerifyBasicsOfSqlCommand(cmd);
            Assert.Empty(cmd.Parameters);
        }

        [Fact]
        public void CreateTempInsertUninitializedItemCmd_Should_Create_SqlCommand_With_Right_Parameters()
        {
            var helper = new SqlCommandHelper(SqlCommandTimeout);

            var cmd = helper.CreateTempInsertUninitializedItemCmd(SqlStatement, SessionId, BufferLength, Buffer, SessionTimeout);

            VerifyBasicsOfSqlCommand(cmd);
            VerifySessionIdParameter(cmd);
            VerifySessionItemLongParameter(cmd);
            VerifyTimeoutParameter(cmd);
            Assert.Equal(3, cmd.Parameters.Count);
        }

        [Theory]
        [InlineData(null)]
        [InlineData(LockId)]
        public void CreateReleaseItemExclusiveCmd_Should_Create_SqlCommand_With_Right_Parameters(object lockId)
        {
            var helper = new SqlCommandHelper(SqlCommandTimeout);

            var cmd = helper.CreateReleaseItemExclusiveCmd(SqlStatement, SessionId, lockId);

            VerifyBasicsOfSqlCommand(cmd);
            VerifySessionIdParameter(cmd);
            VerifyLockCookieParameter(cmd, lockId);
            Assert.Equal(2, cmd.Parameters.Count);
        }

        [Theory]
        [InlineData(null)]
        [InlineData(LockId)]
        public void CreateRemoveStateItemCmd_Should_Create_SqlCommand_With_Right_Parameters(object lockId)
        {
            var helper = new SqlCommandHelper(SqlCommandTimeout);

            var cmd = helper.CreateRemoveStateItemCmd(SqlStatement, SessionId, lockId);

            VerifyBasicsOfSqlCommand(cmd);
            VerifySessionIdParameter(cmd);
            VerifyLockCookieParameter(cmd, lockId);
            Assert.Equal(2, cmd.Parameters.Count);
        }

        [Fact]
        public void CreateResetItemTimeoutCmd_Should_Create_SqlCommand_With_Right_Parameters()
        {
            var helper = new SqlCommandHelper(SqlCommandTimeout);

            var cmd = helper.CreateResetItemTimeoutCmd(SqlStatement, SessionId);

            VerifyBasicsOfSqlCommand(cmd);
            VerifySessionIdParameter(cmd);
            Assert.Equal(1, cmd.Parameters.Count);
        }

        [Fact]
        public void CreateUpdateStateItemLongCmd_Should_Create_SqlCommand_With_Right_Parameters()
        {
            var helper = new SqlCommandHelper(SqlCommandTimeout);

            var cmd = helper.CreateUpdateStateItemLongCmd(SqlStatement, SessionId, Buffer, BufferLength, SessionTimeout, LockId);

            VerifyBasicsOfSqlCommand(cmd);
            VerifySessionIdParameter(cmd);
            VerifySessionItemLongParameter(cmd);
            VerifyTimeoutParameter(cmd);
            VerifyLockCookieParameter(cmd, LockId);
            Assert.Equal(4, cmd.Parameters.Count);
        }

        [Fact]
        public void CreateInsertStateItemLongCmd_Should_Create_SqlCommand_With_Right_Parameters()
        {
            var helper = new SqlCommandHelper(SqlCommandTimeout);

            var cmd = helper.CreateInsertStateItemLongCmd(SqlStatement, SessionId, Buffer, BufferLength, SessionTimeout);

            VerifyBasicsOfSqlCommand(cmd);
            VerifySessionIdParameter(cmd);
            VerifySessionItemLongParameter(cmd);
            VerifyTimeoutParameter(cmd);
            Assert.Equal(3, cmd.Parameters.Count);
        }

        private void VerifyBasicsOfSqlCommand(SqlCommand cmd)
        {
            Assert.Equal(SqlStatement, cmd.CommandText);
            Assert.Equal(CommandType.Text, cmd.CommandType);
            Assert.Equal(SqlCommandTimeout, cmd.CommandTimeout);
        }

        private void VerifySessionIdParameter(SqlCommand cmd)
        {
            var param = cmd.Parameters[SqlParameterName.SessionId];
            Assert.NotNull(param);
            Assert.Equal(SqlDbType.NVarChar, param.SqlDbType);
            Assert.Equal(SessionId, param.Value);
            Assert.Equal(SqlSessionStateRepositoryUtil.IdLength, param.Size);
        }

        private void VerifyLockAgeParameter(SqlCommand cmd)
        {
            var param = cmd.Parameters[SqlParameterName.LockAge];
            Assert.NotNull(param);
            Assert.Equal(SqlDbType.Int, param.SqlDbType);
            Assert.Equal(Convert.DBNull, param.Value);
            Assert.Equal(ParameterDirection.Output, param.Direction);
        }

        private void VerifyLockedParameter(SqlCommand cmd)
        {
            var param = cmd.Parameters[SqlParameterName.Locked];
            Assert.NotNull(param);
            Assert.Equal(SqlDbType.Bit, param.SqlDbType);
            Assert.Equal(Convert.DBNull, param.Value);
            Assert.Equal(ParameterDirection.Output, param.Direction);
        }

        private void VerifyLockCookieParameter(SqlCommand cmd, object lockId = null)
        {
            var param = cmd.Parameters[SqlParameterName.LockCookie];
            Assert.NotNull(param);
            Assert.Equal(SqlDbType.Int, param.SqlDbType);
            if(lockId == null)
            {
                Assert.Equal(Convert.DBNull, param.Value);
                Assert.Equal(ParameterDirection.Output, param.Direction);
            }
            else
            {
                Assert.Equal(lockId, param.Value);
            }
        }

        private void VerifyActionFlagsParameter(SqlCommand cmd)
        {
            var param = cmd.Parameters[SqlParameterName.ActionFlags];
            Assert.NotNull(param);
            Assert.Equal(SqlDbType.Int, param.SqlDbType);
            Assert.Equal(Convert.DBNull, param.Value);
            Assert.Equal(ParameterDirection.Output, param.Direction);
        }

        private void VerifySessionItemLongParameter(SqlCommand cmd)
        {
            var param = cmd.Parameters[SqlParameterName.SessionItemLong];
            Assert.NotNull(param);
            Assert.Equal(SqlDbType.Image, param.SqlDbType);
            Assert.Equal(BufferLength, param.Size);
            Assert.Equal(Buffer, param.Value);            
        }

        private void VerifyTimeoutParameter(SqlCommand cmd)
        {
            var param = cmd.Parameters[SqlParameterName.Timeout];
            Assert.NotNull(param);
            Assert.Equal(SqlDbType.Int, param.SqlDbType);
        }
    }
}
