// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See the License.txt file in the project root for full license information.

namespace Microsoft.AspNet.SessionState.SqlSessionStateAsyncProvider.Test
{
    using Microsoft.Data.SqlClient;
    using System;
    using System.Data;
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

            var cmd = helper.CreateSqlCommand(SqlStatement);

            VerifyBasicsOfSqlCommand(cmd);
            Assert.Empty(cmd.Parameters);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        [InlineData(null)]
        public void SqlCommand_AddSessionIdParameter(bool? fxCompat)
        {
            var helper = new SqlCommandHelper(SqlCommandTimeout);
            var cmd = helper.CreateSqlCommand(SqlStatement);

            if (fxCompat != null)
                cmd.Parameters.AddSessionIdParameter(SessionId, fxCompat.Value);
            else
                cmd.Parameters.AddSessionIdParameter(SessionId);
            VerifyBasicsOfSqlCommand(cmd);
            VerifySessionIdParameter(cmd, fxCompat);
        }

        [Fact]
        public void SqlCommand_AddLockedParameter()
        {
            var helper = new SqlCommandHelper(SqlCommandTimeout);
            var cmd = helper.CreateSqlCommand(SqlStatement);

            cmd.Parameters.AddLockedParameter();
            VerifyBasicsOfSqlCommand(cmd);
            VerifyLockedParameter(cmd);
        }

        [Fact]
        public void SqlCommand_AddLockAgeParameter()
        {
            var helper = new SqlCommandHelper(SqlCommandTimeout);
            var cmd = helper.CreateSqlCommand(SqlStatement);

            cmd.Parameters.AddLockAgeParameter();
            VerifyBasicsOfSqlCommand(cmd);
            VerifyLockAgeParameter(cmd);
        }

        [Theory]
        [InlineData(LockId)]
        [InlineData(null)]
        public void SqlCommand_AddLockCookieParameter(object lockId)
        {
            var helper = new SqlCommandHelper(SqlCommandTimeout);
            var cmd = helper.CreateSqlCommand(SqlStatement);

            cmd.Parameters.AddLockCookieParameter(lockId);
            VerifyBasicsOfSqlCommand(cmd);
            VerifyLockCookieParameter(cmd, lockId);
        }

        [Fact]
        public void SqlCommand_AddLockDateParameter()
        {
            var helper = new SqlCommandHelper(SqlCommandTimeout);
            var cmd = helper.CreateSqlCommand(SqlStatement);

            cmd.Parameters.AddLockDateParameter();
            VerifyBasicsOfSqlCommand(cmd);
            VerifyLockDateParameter(cmd);
        }

        [Fact]
        public void SqlCommand_AddActionFlagsParameter()
        {
            var helper = new SqlCommandHelper(SqlCommandTimeout);
            var cmd = helper.CreateSqlCommand(SqlStatement);

            cmd.Parameters.AddActionFlagsParameter();
            VerifyBasicsOfSqlCommand(cmd);
            VerifyActionFlagsParameter(cmd);
        }

        [Fact]
        public void SqlCommand_AddTimeoutParameter()
        {
            var helper = new SqlCommandHelper(SqlCommandTimeout);
            var cmd = helper.CreateSqlCommand(SqlStatement);

            cmd.Parameters.AddTimeoutParameter(SessionTimeout);
            VerifyBasicsOfSqlCommand(cmd);
            VerifyTimeoutParameter(cmd, SessionTimeout);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        [InlineData(null)]
        public void SqlCommand_AddSessionItemLongImageParameter(bool? fxCompat)
        {
            var helper = new SqlCommandHelper(SqlCommandTimeout);
            var cmd = helper.CreateSqlCommand(SqlStatement);

            if (fxCompat != null)
                cmd.Parameters.AddSessionItemLongImageParameter(BufferLength, Buffer, fxCompat.Value);
            else
                cmd.Parameters.AddSessionItemLongImageParameter(BufferLength, Buffer);

            VerifyBasicsOfSqlCommand(cmd);
            VerifySessionItemLongParameter(cmd, SqlDbType.Image, BufferLength, Buffer, fxCompat);
        }

        [Fact]
        public void SqlCommand_AddSessionItemLongVarBinaryParameter()
        {
            var helper = new SqlCommandHelper(SqlCommandTimeout);
            var cmd = helper.CreateSqlCommand(SqlStatement);

            cmd.Parameters.AddSessionItemLongVarBinaryParameter(BufferLength, Buffer);
            VerifyBasicsOfSqlCommand(cmd);
            VerifySessionItemLongParameter(cmd, SqlDbType.VarBinary, BufferLength, Buffer, false);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        [InlineData(null)]
        public void SqlCommand_AddSessionItemShortParameter(bool? fxCompat)
        {
            var helper = new SqlCommandHelper(SqlCommandTimeout);
            var cmd = helper.CreateSqlCommand(SqlStatement);

            if (fxCompat != null)
                cmd.Parameters.AddSessionItemShortParameter(BufferLength, Buffer, fxCompat.Value);
            else
                cmd.Parameters.AddSessionItemShortParameter(BufferLength, Buffer);
            VerifyBasicsOfSqlCommand(cmd);
            VerifySessionItemShortParameter(cmd, BufferLength, Buffer, fxCompat);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        [InlineData(null)]
        public void SqlCommand_AddSessionItemShortOutputParameter(bool? fxCompat)
        {
            var helper = new SqlCommandHelper(SqlCommandTimeout);
            var cmd = helper.CreateSqlCommand(SqlStatement);

            if (fxCompat != null)
                cmd.Parameters.AddSessionItemShortParameter(fxCompat: fxCompat.Value);
            else
                cmd.Parameters.AddSessionItemShortParameter();
            VerifyBasicsOfSqlCommand(cmd);
            VerifySessionItemShortParameter(cmd, fxCompat: fxCompat);
        }

        private void VerifyBasicsOfSqlCommand(SqlCommand cmd)
        {
            Assert.Equal(SqlStatement, cmd.CommandText);
            Assert.Equal(CommandType.Text, cmd.CommandType);
            Assert.Equal(SqlCommandTimeout, cmd.CommandTimeout);
        }

        private void VerifySessionIdParameter(SqlCommand cmd, bool? fxCompat)
        {
            var param = cmd.Parameters[(fxCompat != null && fxCompat.Value) ? SqlParameterName.Compat_SessionId : SqlParameterName.SessionId];
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

        private void VerifyLockDateParameter(SqlCommand cmd)
        {
            var param = cmd.Parameters[SqlParameterName.LockDate];
            Assert.NotNull(param);
            Assert.Equal(SqlDbType.DateTime, param.SqlDbType);
            Assert.Equal(Convert.DBNull, param.Value);
            Assert.Equal(ParameterDirection.Output, param.Direction);
        }

        private void VerifyActionFlagsParameter(SqlCommand cmd)
        {
            var param = cmd.Parameters[SqlParameterName.ActionFlags];
            Assert.NotNull(param);
            Assert.Equal(SqlDbType.Int, param.SqlDbType);
            Assert.Equal(Convert.DBNull, param.Value);
            Assert.Equal(ParameterDirection.Output, param.Direction);
        }

        private void VerifySessionItemLongParameter(SqlCommand cmd, SqlDbType sqlType, int length, byte[] buf, bool? fxCompat)
        {
            var param = cmd.Parameters[(fxCompat != null && fxCompat.Value) ? SqlParameterName.Compat_ItemLong : SqlParameterName.SessionItemLong];
            Assert.NotNull(param);
            Assert.Equal(sqlType, param.SqlDbType);
            Assert.Equal(length, param.Size);
            Assert.Equal(buf, param.Value);            
        }

        private void VerifySessionItemShortParameter(SqlCommand cmd, int length = 0, byte[] buf = null, bool? fxCompat = null)
        {
            var param = cmd.Parameters[(fxCompat != null && fxCompat.Value) ? SqlParameterName.Compat_ItemShort : SqlParameterName.SessionItemShort];
            Assert.NotNull(param);
            Assert.Equal(SqlDbType.VarBinary, param.SqlDbType);

            if (buf == null)
            {
                Assert.Equal(ParameterDirection.Output, param.Direction);
                Assert.Equal(SqlSessionStateRepositoryUtil.ITEM_SHORT_LENGTH, param.Size);
            }
            else
            {
                Assert.Equal(length, param.Size);
                Assert.Equal(buf, param.Value);
            }
        }

        private void VerifyTimeoutParameter(SqlCommand cmd, int timeout)
        {
            var param = cmd.Parameters[SqlParameterName.Timeout];
            Assert.NotNull(param);
            Assert.Equal(SqlDbType.Int, param.SqlDbType);
            Assert.Equal(timeout, param.Value);
        }
    }
}
