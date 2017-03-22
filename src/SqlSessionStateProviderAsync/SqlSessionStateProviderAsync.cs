// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNet.SessionState
{
    using System;
    using System.Threading.Tasks;
    using System.Threading;
    using System.Web;
    using System.Web.SessionState;
    using System.Globalization;
    using System.Configuration;
    using System.IO;
    using System.IO.Compression;
    using System.Diagnostics;
    using System.Web.Configuration;
    using System.Security;
    using System.Configuration.Provider;
    using System.Collections.Specialized;
    using Resources;
    using System.Data.SqlClient;

    /// <summary>
    /// Async version of SqlSessionState provider based on EF
    /// </summary>
    public class SqlSessionStateProviderAsync : SessionStateStoreProviderAsyncBase
    {
        private const double SessionExpiresFrequencyCheckIntervalTicks = 30 * TimeSpan.TicksPerSecond;
        private static long s_lastSessionPurgeTicks;
        private static int s_inPurge;
        private static string s_appSuffix;

        private int _rqOrigStreamLen;
        private ISqlStateCommandCreator _sqlCommandCreator;

        private ConnectionStringSettings ConnectionString { get; set; }
        private bool CompressionEnabled { get; set; }
        
        /// <summary>
        /// Initialize the provider through the configuration
        /// </summary>
        /// <param name="name">Sessionstate provider name</param>
        /// <param name="config">Configuration values</param>
        public override void Initialize(string name, NameValueCollection config)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }
            if (String.IsNullOrEmpty(name))
            {
                name = "SqlSessionStateAsyncProvider";
            }

            base.Initialize(name, config);

            ConnectionString = ConfigurationHelper.GetConnectionString(config["connectionStringName"]);
            config.Remove("connectionStringName");

            try
            {
                SessionStateSection ssc = (SessionStateSection)ConfigurationManager.GetSection("system.web/sessionState");
                CompressionEnabled = ssc.CompressionEnabled;
                SqlStateExecutor.Initialize(ConnectionString.ConnectionString, ssc.SqlConnectionRetryInterval);

                // check if it's inMemory table
                _sqlCommandCreator = new SqlStateCommandCreator((int)ssc.SqlCommandTimeout.TotalSeconds);

                // must be called after SqlStateExecutor & SqlStateCommandFactory are initialized
                SqlStateExecutor.CreateSessionStateTableIfNeeded(_sqlCommandCreator);
            }
            catch (SecurityException)
            {
                // We don't want to blow up in medium trust, just turn compression enabled off instead
            }

            if (config.Count > 0)
            {
                string key = config.GetKey(0);
                if (!string.IsNullOrEmpty(key))
                {
                    throw new ProviderException(String.Format(CultureInfo.CurrentCulture, SR.Provider_unrecognized_attribute, key));
                }
            }

            if (s_appSuffix == null)
            {
                string appId = HttpRuntime.AppDomainAppId;
                s_appSuffix = appId.GetHashCode().ToString("X8", CultureInfo.InvariantCulture);
            }

        }

        /// <summary>
        /// Create a new SessionStateStoreData
        /// </summary>
        /// <param name="context">Httpcontext</param>
        /// <param name="timeout">Session timeout</param>
        /// <returns></returns>
        public override SessionStateStoreData CreateNewStoreData(HttpContextBase context, int timeout)
        {
            HttpStaticObjectsCollection staticObjects = null;
            if (context != null)
            {
                staticObjects = SessionStateUtility.GetSessionStaticObjects(context.ApplicationInstance.Context);
            }

            return new SessionStateStoreData(new SessionStateItemCollection(), staticObjects, timeout);
        }

        /// <inheritdoc />
        public override async Task CreateUninitializedItemAsync(
            HttpContextBase context, 
            string id, 
            int timeout, 
            CancellationToken cancellationToken)
        {
            if (id == null)
            {
                throw new ArgumentNullException("id");
            }
            if (id.Length > SessionIDManager.SessionIDMaxLength)
            {
                throw new ArgumentException(SR.Session_id_too_long);
            }
            id = AppendAppIdHash(id);
            byte[] buf;
            int length;

            var item = new SessionStateStoreData(new SessionStateItemCollection(),
                        SessionStateUtility.GetSessionStaticObjects(context.ApplicationInstance.Context),
                        timeout);

            SerializeStoreData(item, SqlStateCommandUtil.ItemShortLength, out buf, out length, CompressionEnabled);
            var cmd = _sqlCommandCreator.CreateTempInsertUninitializedItemCmd(id, length, buf, timeout);
            using (var executor = GetSqlStateExecutor(cmd))
            {
                await executor.SqlExecuteNonQueryWithRetryAsync(true);
            }
        }

        /// <inheritdoc />
        public override void Dispose()
        {
        }

        /// <inheritdoc />
        public override Task EndRequestAsync(HttpContextBase context)
        {
            PurgeIfNeeded();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override Task<GetItemResult> GetItemAsync(HttpContextBase context, string id, CancellationToken cancellationToken)
        {
            return DoGet(context, id, false, cancellationToken);
        }

        /// <inheritdoc />
        public override Task<GetItemResult> GetItemExclusiveAsync(
            HttpContextBase context, 
            string id, 
            CancellationToken cancellationToken)
        {
            return DoGet(context, id, true, cancellationToken);
        }

        /// <inheritdoc />
        public override void InitializeRequest(HttpContextBase context)
        {
            _rqOrigStreamLen = 0;
        }

        /// <inheritdoc />
        public override async Task ReleaseItemExclusiveAsync(
            HttpContextBase context, 
            string id, 
            object lockId, 
            CancellationToken cancellationToken)
        {
            if (id == null)
            {
                throw new ArgumentNullException("id");
            }
            if (id.Length > SessionIDManager.SessionIDMaxLength)
            {
                throw new ArgumentException(SR.Session_id_too_long);
            }

            id = AppendAppIdHash(id);
            var cmd = _sqlCommandCreator.CreateReleaseItemExclusiveCmd(id, lockId);
            using(var executor = GetSqlStateExecutor(cmd))
            {
                await executor.SqlExecuteNonQueryWithRetryAsync();
            }
        }

        /// <inheritdoc />
        public override async Task RemoveItemAsync(
            HttpContextBase context, 
            string id, 
            object lockId, 
            SessionStateStoreData item, 
            CancellationToken cancellationToken)
        {
            if (id == null)
            {
                throw new ArgumentNullException("id");
            }
            if (id.Length > SessionIDManager.SessionIDMaxLength)
            {
                throw new ArgumentException(SR.Session_id_too_long);
            }

            id = AppendAppIdHash(id);

            var cmd = _sqlCommandCreator.CreateRemoveStateItemCmd(id, lockId);
            using(var executor = GetSqlStateExecutor(cmd))
            {
                await executor.SqlExecuteNonQueryWithRetryAsync();
            }
        }

        /// <inheritdoc />
        public override async Task ResetItemTimeoutAsync(
            HttpContextBase context, 
            string id, 
            CancellationToken cancellationToken)
        {
            if (id == null)
            {
                throw new ArgumentNullException("id");
            }
            if (id.Length > SessionIDManager.SessionIDMaxLength)
            {
                throw new ArgumentException(SR.Session_id_too_long);
            }

            id = AppendAppIdHash(id);

            var cmd = _sqlCommandCreator.CreateResetItemTimeoutCmd(id);
            using(var executor = GetSqlStateExecutor(cmd))
            {
                await executor.SqlExecuteNonQueryWithRetryAsync();
            }
        }

        /// <inheritdoc />
        public override async Task SetAndReleaseItemExclusiveAsync(
            HttpContextBase context, 
            string id, 
            SessionStateStoreData item, 
            object lockId, 
            bool newItem, 
            CancellationToken cancellationToken)
        {
            byte[] buf;
            int length;
            int lockCookie;
            SqlCommand cmd;

            if (item == null)
            {
                throw new ArgumentNullException("item");
            }
            if (id == null)
            {
                throw new ArgumentNullException("id");
            }
            if (id.Length > SessionIDManager.SessionIDMaxLength)
            {
                throw new ArgumentException(SR.Session_id_too_long);
            }
            id = AppendAppIdHash(id);

            try
            {
                SerializeStoreData(item, SqlStateCommandUtil.ItemShortLength, out buf, out length, CompressionEnabled);
            }
            catch
            {
                if(!newItem)
                {
                    await ReleaseItemExclusiveAsync(context, id, lockId, cancellationToken);
                }
                throw;
            }

            lockCookie = lockId == null ? 0 : (int)lockId;

            if (!newItem)
            {
                if(length <= SqlStateCommandUtil.ItemShortLength)
                {
                    cmd = _rqOrigStreamLen <= SqlStateCommandUtil.ItemShortLength ?
                        _sqlCommandCreator.CreateUpdateStateItemShortCmd(id, buf, length, item.Timeout, lockCookie) :
                        _sqlCommandCreator.CreateUpdateStateItemShortNullLongCmd(id, buf, length, item.Timeout, lockCookie);
                }
                else
                {
                    cmd = _rqOrigStreamLen <= SqlStateCommandUtil.ItemShortLength ?
                        _sqlCommandCreator.CreateUpdateStateItemLongNullShortCmd(id, buf, length, item.Timeout, lockCookie) :
                        _sqlCommandCreator.CreateUpdateStateItemLongCmd(id, buf, length, item.Timeout, lockCookie);
                }                
            }
            else
            {
                cmd = length <= SqlStateCommandUtil.ItemShortLength ?
                    _sqlCommandCreator.CreateInsertStateItemShortCmd(id, buf, length, item.Timeout) :
                    _sqlCommandCreator.CreateInsertStateItemLongCmd(id, buf, length, item.Timeout);
            }

            using(var executor = new SqlStateExecutor(cmd))
            {
                await executor.SqlExecuteNonQueryWithRetryAsync(newItem);
            }
        }

        /// <inheritdoc />
        public override bool SetItemExpireCallback(System.Web.SessionState.SessionStateItemExpireCallback expireCallback)
        {
            return false;
        }

        private async Task<GetItemResult> DoGet(HttpContextBase context, string id, bool exclusive, CancellationToken cancellationToken)
        {
            if (id.Length > SessionIDManager.SessionIDMaxLength)
            {
                throw new ArgumentException(SR.Session_id_too_long);
            }
            id = AppendAppIdHash(id);
            var locked = false;
            var lockAge = TimeSpan.Zero;
            var now = DateTime.UtcNow;

            object lockId = null;
            byte[] buf = null;
            SessionStateActions actions = SessionStateActions.None;
            SessionStateStoreData item = null;
            SqlCommand cmd = null;

            if (exclusive)
            {
                cmd = _sqlCommandCreator.CreateGetStateItemExclusiveCmd(id);
            }
            else
            {
                cmd = _sqlCommandCreator.CreateGetStateItemCmd(id);
            }

            using (var executor = new SqlStateExecutor(cmd))
            {
                using (var reader = await executor.SqlExecuteReaderWithRetryAsync())
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
                        SqlStateExecutor.ThrowSqlConnectionException(ex);
                    }
                }

                var outParameterLocked = executor.GetOutPutParameterValue(SqlParameterName.Locked);
                if (outParameterLocked == null || Convert.IsDBNull(outParameterLocked.Value))
                {
                    return null;
                }
                locked = (bool)outParameterLocked.Value;
                lockId = (int)executor.GetOutPutParameterValue(SqlParameterName.LockCookie).Value;

                if (locked)
                {
                    lockAge = new TimeSpan(0, 0, (int)executor.GetOutPutParameterValue(SqlParameterName.LockAge).Value);

                    if (lockAge > new TimeSpan(0, 0, Sec.ONE_YEAR))
                    {
                        lockAge = TimeSpan.Zero;
                    }
                    return new GetItemResult(null, true, lockAge, lockId, actions);
                }
                actions = (SessionStateActions)executor.GetOutPutParameterValue(SqlParameterName.ActionFlags).Value;

                if (buf == null)
                {
                    buf = (byte[])executor.GetOutPutParameterValue(SqlParameterName.SessionItemShort).Value;
                }
            }

            using (var stream = new MemoryStream(buf))
            {
                item = DeserializeStoreData(context, stream, CompressionEnabled);
                _rqOrigStreamLen = (int)stream.Position;
            }

            return new GetItemResult(item, locked, lockAge, lockId, actions);
        }

        private SqlStateExecutor GetSqlStateExecutor(SqlCommand cmd)
        {
            return new SqlStateExecutor(cmd);
        }

        // We just want to append an 8 char hash from the AppDomainAppId to prevent any session id collisions
        private static string AppendAppIdHash(string id)
        {
            if (!id.EndsWith(s_appSuffix))
            {
                return id + s_appSuffix;
            }
            return id;
        }        

        // Internal code copied from SessionStateUtility
        private static void SerializeStoreData(
            SessionStateStoreData item, 
            int initialStreamSize, 
            out byte[] buf, 
            out int length, 
            bool compressionEnabled)
        {
            using (MemoryStream s = new MemoryStream(initialStreamSize))
            {
                Serialize(item, s);
                if (compressionEnabled)
                {
                    byte[] serializedBuffer = s.GetBuffer();
                    int serializedLength = (int)s.Length;
                    // truncate the MemoryStream so we can write the compressed data in it
                    s.SetLength(0);
                    // compress the serialized bytes
                    using (DeflateStream zipStream = new DeflateStream(s, CompressionMode.Compress, true))
                    {
                        zipStream.Write(serializedBuffer, 0, serializedLength);
                    }
                    // if the session state tables have ANSI_PADDING disabled, last )s are trimmed.
                    // This shouldn't happen, but to be sure, we are padding with an extra byte
                    s.WriteByte((byte)0xff);
                }
                buf = s.GetBuffer();
                length = (int)s.Length;
            }
        }

        private static void Serialize(SessionStateStoreData item, Stream stream)
        {
            bool hasItems = true;
            bool hasStaticObjects = true;

            BinaryWriter writer = new BinaryWriter(stream);
            writer.Write(item.Timeout);

            if (item.Items == null || item.Items.Count == 0)
            {
                hasItems = false;
            }
            writer.Write(hasItems);

            if (item.StaticObjects == null || item.StaticObjects.NeverAccessed)
            {
                hasStaticObjects = false;
            }
            writer.Write(hasStaticObjects);

            if (hasItems)
            {
                ((SessionStateItemCollection)item.Items).Serialize(writer);
            }

            if (hasStaticObjects)
            {
                item.StaticObjects.Serialize(writer);
            }

            // Prevent truncation of the stream
            writer.Write(unchecked((byte)0xff));
        }

        private static SessionStateStoreData DeserializeStoreData(HttpContextBase context, Stream stream, bool compressionEnabled)
        {
            if (compressionEnabled)
            {
                // apply the compression decorator on top of the stream
                // the data should not be bigger than 4GB - compression doesn't work for more than that
                using (DeflateStream zipStream = new DeflateStream(stream, CompressionMode.Decompress, true))
                {
                    return Deserialize(context, zipStream);
                }
            }
            return Deserialize(context, stream);
        }

        private static SessionStateStoreData Deserialize(HttpContextBase context, Stream stream)
        {
            int timeout;
            SessionStateItemCollection sessionItems;
            bool hasItems;
            bool hasStaticObjects;
            HttpStaticObjectsCollection staticObjects;
            byte eof;

            Debug.Assert(context != null);

            try
            {
                BinaryReader reader = new BinaryReader(stream);
                timeout = reader.ReadInt32();
                hasItems = reader.ReadBoolean();
                hasStaticObjects = reader.ReadBoolean();

                if (hasItems)
                {
                    sessionItems = SessionStateItemCollection.Deserialize(reader);
                }
                else {
                    sessionItems = new SessionStateItemCollection();
                }

                if (hasStaticObjects)
                {
                    staticObjects = HttpStaticObjectsCollection.Deserialize(reader);
                }
                else {
                    staticObjects = SessionStateUtility.GetSessionStaticObjects(context.ApplicationInstance.Context);
                }

                eof = reader.ReadByte();
                if (eof != 0xff)
                {
                    throw new HttpException(String.Format(CultureInfo.CurrentCulture, SR.Invalid_session_state));
                }
            }
            catch (EndOfStreamException)
            {
                throw new HttpException(String.Format(CultureInfo.CurrentCulture, SR.Invalid_session_state));
            }

            return new SessionStateStoreData(sessionItems, staticObjects, timeout);
        }

        private bool CanPurge()
        {
            return (
                DateTime.UtcNow.Ticks - s_lastSessionPurgeTicks > SessionExpiresFrequencyCheckIntervalTicks
                && Interlocked.CompareExchange(ref s_inPurge, 1, 0) == 0
                );
        }

        private void PurgeIfNeeded()
        {
            // Only check for expired sessions every 30 seconds.
            if (CanPurge())
            {
                Task.Run(() => PurgeExpiredSessions());
            }
        }

        private void PurgeExpiredSessions()
        {
            try
            {
                using(var executor = new SqlStateExecutor(_sqlCommandCreator.CreateDeleteExpiredSessionsCmd()))
                {
                    var task = executor.SqlExecuteNonQueryWithRetryAsync().ConfigureAwait(false);
                    task.GetAwaiter().GetResult();
                    s_lastSessionPurgeTicks = DateTime.Now.Ticks;
                }                
            }
            catch
            {
                // Swallow all failures, this is called from an async Task and we don't want to crash
            }
            finally
            {
                Interlocked.CompareExchange(ref s_inPurge, 0, 1);
            }
        }
    }
}
