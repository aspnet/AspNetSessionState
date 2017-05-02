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
    using System.Configuration.Provider;
    using System.Collections.Specialized;
    using Resources;

    /// <summary>
    /// Async version of SqlSessionState provider based on EF
    /// </summary>
    public class SqlSessionStateProviderAsync : SessionStateStoreProviderAsyncBase
    {
        private const string INMEMORY_TABLE_CONFIGURATION_NAME = "UseInMemoryTable";
        private const string MAX_RETRY_NUMBER_CONFIGURATION_NAME = "MaxRetryNumber";
        private const string RETRY_INTERVAL_CONFIGURATION_NAME = "RetryInterval";
        private const string CONNECTIONSTRING_NAME_CONFIGURATION_NAME = "connectionStringName";
        private const double SessionExpiresFrequencyCheckIntervalTicks = 30 * TimeSpan.TicksPerSecond;
        private static long s_lastSessionPurgeTicks;
        private static int s_inPurge;
        private static string s_appSuffix;
        private static bool s_compressionEnabled;
        private static bool s_oneTimeInited = false;
        private static object s_lock = new object();
        private static ISqlSessionStateRepository s_sqlSessionStateRepository;
        
        private int _rqOrigStreamLen;
                
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

            if (!s_oneTimeInited)
            {
                lock (s_lock)
                {
                    if(!s_oneTimeInited)
                    {
                        var connectionString = GetConnectionString(config[CONNECTIONSTRING_NAME_CONFIGURATION_NAME]);
                        SessionStateSection ssc = (SessionStateSection)ConfigurationManager.GetSection("system.web/sessionState");
                        s_compressionEnabled = ssc.CompressionEnabled;

                        if (ShouldUseInMemoryTable(config))
                        {
                            s_sqlSessionStateRepository = new SqlInMemoryTableSessionStateRepository(connectionString.ConnectionString,
                                (int)ssc.SqlCommandTimeout.TotalSeconds, GetRetryInterval(config), GetMaxRetryNum(config));
                        }
                        else
                        {
                            s_sqlSessionStateRepository = new SqlSessionStateRepository(connectionString.ConnectionString, 
                                (int)ssc.SqlCommandTimeout.TotalSeconds, GetRetryInterval(config), GetMaxRetryNum(config));
                        }
                        s_sqlSessionStateRepository.CreateSessionStateTable();

                        Debug.Assert(HttpRuntime.AppDomainAppId != null);
                        s_appSuffix = HttpRuntime.AppDomainAppId.GetHashCode().ToString("X8", CultureInfo.InvariantCulture);

                        s_oneTimeInited = true;
                    }
                }
            }
        }

        private bool ShouldUseInMemoryTable(NameValueCollection config)
        {
            var useInMemoryTable = false;
            var val = config[INMEMORY_TABLE_CONFIGURATION_NAME];
            return (val != null && bool.TryParse(val, out useInMemoryTable) && useInMemoryTable);
        }

        private int? GetMaxRetryNum(NameValueCollection config)
        {
            int maxRetryNum;
            var val = config[MAX_RETRY_NUMBER_CONFIGURATION_NAME];
            if (val != null && int.TryParse(val, out maxRetryNum))
            {
                return maxRetryNum;
            }
            return null;
        }

        private int? GetRetryInterval(NameValueCollection config)
        {
            int retryInterval;
            var val = config[RETRY_INTERVAL_CONFIGURATION_NAME];
            if(val != null && int.TryParse(val, out retryInterval))
            {
                return retryInterval;
            }
            return null;
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

            SerializeStoreData(item, SqlSessionStateRepositoryUtil.DefaultItemLength, out buf, out length, s_compressionEnabled);
            await s_sqlSessionStateRepository.CreateUninitializedSessionItemAsync(id, length, buf, timeout);
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

            await s_sqlSessionStateRepository.ReleaseSessionItemAsync(id, lockId);
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

            await s_sqlSessionStateRepository.RemoveSessionItemAsync(id, lockId);
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

            await s_sqlSessionStateRepository.ResetSessionItemTimeoutAsync(id);
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
                SerializeStoreData(item, SqlSessionStateRepositoryUtil.DefaultItemLength, out buf, out length, s_compressionEnabled);
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

            await s_sqlSessionStateRepository.CreateOrUpdateSessionStateItemAsync(newItem, id, buf, length, item.Timeout, lockCookie, _rqOrigStreamLen);
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
            
            SessionStateStoreData data = null;
            var sessionItem = await s_sqlSessionStateRepository.GetSessionStateItemAsync(id, exclusive);

            if(sessionItem == null)
            {
                return null;
            }
            if(sessionItem.Item == null)
            {
                return new GetItemResult(null, sessionItem.Locked, sessionItem.LockAge, sessionItem.LockId, sessionItem.Actions);
            }

            using (var stream = new MemoryStream(sessionItem.Item))
            {
                data = DeserializeStoreData(context, stream, s_compressionEnabled);
                _rqOrigStreamLen = (int)stream.Position;
            }

            return new GetItemResult(data, sessionItem.Locked, sessionItem.LockAge, sessionItem.LockId, sessionItem.Actions);
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
                s_sqlSessionStateRepository.DeleteExpiredSessions();
                s_lastSessionPurgeTicks = DateTime.UtcNow.Ticks;
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

        private static ConnectionStringSettings GetConnectionString(string connectionstringName)
        {
            if (string.IsNullOrEmpty(connectionstringName))
            {
                throw new ProviderException(SR.Connection_name_not_specified);
            }
            ConnectionStringSettings conn = ConfigurationManager.ConnectionStrings[connectionstringName];
            if (conn == null)
            {
                throw new ProviderException(
                    String.Format(CultureInfo.CurrentCulture, SR.Connection_string_not_found, connectionstringName));
            }
            return conn;
        }        
    }
}
