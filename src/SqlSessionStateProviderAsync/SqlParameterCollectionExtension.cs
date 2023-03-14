// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See the License.txt file in the project root for full license information.

namespace Microsoft.AspNet.SessionState
{
    using Microsoft.Data.SqlClient;
    using System;
    using System.Data;

    static class SqlParameterCollectionExtension
    {
        public static SqlParameterCollection AddSessionIdParameter(this SqlParameterCollection pc, string id)
        {
            var param = new SqlParameter(SqlParameterName.SessionId, SqlDbType.NVarChar, SqlSessionStateRepositoryUtil.IdLength);
            param.Value = id;
            pc.Add(param);

            return pc;
        }

        public static SqlParameterCollection AddFxSessionIdParameter(this SqlParameterCollection pc, string id)
        {
            var param = new SqlParameter(SqlParameterName.FxSessionId, SqlDbType.NVarChar, SqlSessionStateRepositoryUtil.IdLength);
            param.Value = id;
            pc.Add(param);

            return pc;
        }

        public static SqlParameterCollection AddLockedParameter(this SqlParameterCollection pc)
        {
            var param = new SqlParameter(SqlParameterName.Locked, SqlDbType.Bit);
            param.Direction = ParameterDirection.Output;
            param.Value = Convert.DBNull;
            pc.Add(param);

            return pc;
        }

        public static SqlParameterCollection AddLockAgeParameter(this SqlParameterCollection pc)
        {
            var param = new SqlParameter(SqlParameterName.LockAge, SqlDbType.Int);
            param.Direction = ParameterDirection.Output;
            param.Value = Convert.DBNull;
            pc.Add(param);

            return pc;
        }

        public static SqlParameterCollection AddLockCookieParameter(this SqlParameterCollection pc, object lockId = null)
        {
            var param = new SqlParameter(SqlParameterName.LockCookie, SqlDbType.Int);
            if (lockId == null)
            {
                param.Direction = ParameterDirection.Output;
                param.Value = Convert.DBNull;
            }
            else
            {
                param.Value = lockId;
            }
            pc.Add(param);

            return pc;
        }

        public static SqlParameterCollection AddLockDateParameter(this SqlParameterCollection pc)
        {
            var param = new SqlParameter(SqlParameterName.LockDate, SqlDbType.DateTime);
            param.Direction = ParameterDirection.Output;
            param.Value = Convert.DBNull;
            pc.Add(param);

            return pc;
        }

        public static SqlParameterCollection AddActionFlagsParameter(this SqlParameterCollection pc)
        {
            var param = new SqlParameter(SqlParameterName.ActionFlags, SqlDbType.Int);
            param.Direction = ParameterDirection.Output;
            param.Value = Convert.DBNull;
            pc.Add(param);

            return pc;
        }

        public static SqlParameterCollection AddTimeoutParameter(this SqlParameterCollection pc, int timeout)
        {
            var param = new SqlParameter(SqlParameterName.Timeout, SqlDbType.Int);
            param.Value = timeout;
            pc.Add(param);

            return pc;
        }

        public static SqlParameterCollection AddSessionItemLongImageParameter(this SqlParameterCollection pc, int length, byte[] buf)
        {
            var param = new SqlParameter(SqlParameterName.SessionItemLong, SqlDbType.Image, length);
            param.Value = buf;
            pc.Add(param);

            return pc;
        }

        public static SqlParameterCollection AddSessionItemLongVarBinaryParameter(this SqlParameterCollection pc, int length, byte[] buf)
        {
            SqlParameter param = new SqlParameter(SqlParameterName.SessionItemLong, SqlDbType.VarBinary, length);
            param.Value = buf;
            pc.Add(param);

            return pc;
        }

        public static SqlParameterCollection AddItemLongParameter(this SqlParameterCollection pc, int length, byte[] buf)
        {
            var param = new SqlParameter(SqlParameterName.ItemLong, SqlDbType.Image, length);
            param.Value = buf;
            pc.Add(param);

            return pc;
        }

        public static SqlParameterCollection AddItemShortParameter(this SqlParameterCollection pc, int length = 0, byte[] buf = null)
        {
            SqlParameter param;

            if (buf == null)
            {
                param = new SqlParameter(SqlParameterName.ItemShort, SqlDbType.VarBinary, SqlSessionStateRepositoryUtil.ITEM_SHORT_LENGTH);
                param.Direction = ParameterDirection.Output;
                param.Value = Convert.DBNull;
            }
            else
            {
                param = new SqlParameter(SqlParameterName.ItemShort, SqlDbType.VarBinary, length);
                param.Value = buf;
            }
            pc.Add(param);

            return pc;
        }
    }
}
