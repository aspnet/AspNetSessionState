﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNet.SessionState
{
    using System;
    using System.Data;
    using System.Data.SqlClient;

    internal static class SqlParameterCollectionExtension
    {
        public static SqlParameterCollection AddSessionIdParameter(this SqlParameterCollection pc, string id)
        {
            var param = new SqlParameter($"@{SqlParameterName.SessionId}", SqlDbType.NVarChar, SqlCommandUtil.IdLength);
            param.Value = id;
            pc.Add(param);

            return pc;
        }

        public static SqlParameterCollection AddSessionItemShortParameter(this SqlParameterCollection pc, int length = 0, byte[] buf = null)
        {
            var param = new SqlParameter($"@{SqlParameterName.SessionItemShort}", SqlDbType.VarBinary, SqlCommandUtil.ItemShortLength);
            if (buf != null)
            {
                param.Value = buf;
                param.Size = length;
            }
            else
            {
                param.Direction = ParameterDirection.Output;
                param.Value = Convert.DBNull;
            }
            pc.Add(param);

            return pc;
        }

        public static SqlParameterCollection AddLockedParameter(this SqlParameterCollection pc)
        {
            var param = new SqlParameter($"@{SqlParameterName.Locked}", SqlDbType.Bit);
            param.Direction = ParameterDirection.Output;
            param.Value = Convert.DBNull;
            pc.Add(param);

            return pc;
        }

        public static SqlParameterCollection AddLockAgeParameter(this SqlParameterCollection pc)
        {
            var param = new SqlParameter($"@{SqlParameterName.LockAge}", SqlDbType.Int);
            param.Direction = ParameterDirection.Output;
            param.Value = Convert.DBNull;
            pc.Add(param);

            return pc;
        }

        public static SqlParameterCollection AddLockCookieParameter(this SqlParameterCollection pc, object lockId = null)
        {
            var param = new SqlParameter($"@{SqlParameterName.LockCookie}", SqlDbType.Int);
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

        public static SqlParameterCollection AddActionFlagsParameter(this SqlParameterCollection pc)
        {
            var param = new SqlParameter($"@{SqlParameterName.ActionFlags}", SqlDbType.Int);
            param.Direction = ParameterDirection.Output;
            param.Value = Convert.DBNull;
            pc.Add(param);

            return pc;
        }

        public static SqlParameterCollection AddTimeoutParameter(this SqlParameterCollection pc, int timeout)
        {
            var param = new SqlParameter($"@{SqlParameterName.Timeout}", SqlDbType.Int);
            param.Value = timeout;
            pc.Add(param);

            return pc;
        }

        public static SqlParameterCollection AddSessionItemLongParameter(this SqlParameterCollection pc, int length, byte[] buf)
        {
            var param = new SqlParameter($"@{SqlParameterName.SessionItemLong}", SqlDbType.Image, length);
            param.Value = buf;
            pc.Add(param);

            return pc;
        }
    }
}
