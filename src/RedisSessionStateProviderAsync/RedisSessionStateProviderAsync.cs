using System;
using System.Collections.Specialized;
using System.Configuration;
using System.Configuration.Provider;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Configuration;
using System.Web.SessionState;

using StackExchange.Redis;

namespace Microsoft.AspNet.SessionState
{
    /// <summary>
    /// Async version of Redis SessionState Provider
    /// </summary>
    public class RedisSessionStateProviderAsync : SessionStateStoreProviderAsyncBase
    {
        private static readonly int DefaultLockCookie = 1;

        private static int s_databaseId;
        private static string s_connectionString;
        private static bool s_compressionEnabled;
        private static int s_timeout;
        private static bool s_oneTimeInited;
        private static object s_lock = new object();
        private static ConnectionMultiplexer s_multiplexer;
        private static IDatabase s_connection;

        #region Redis Lua Scripts

        private static readonly string CreateSessionStateItem = @"
redis.replicate_commands()
local time = redis.call('TIME')
local sessionStateItem = { 'lockDate', time[1], 'lockAge', '0', 'lockCookie', ARGV[2], 'locked', '0', 'ttl', ARGV[1], 'sessionItem', ARGV[3], 'uninitialized', ARGV[4] }
redis.call('HMSET', KEYS[1], unpack(sessionStateItem))
redis.call('EXPIRE', KEYS[1], ARGV[1])
return 1";

        private static readonly string GetStateItem = @"
if redis.call('EXISTS', KEYS[1]) == 0 then
  return nil
end
local result = redis.call('HGETALL', KEYS[1])
local item = {}
for idx = 1, #result, 2 do
    item[result[idx]] = result[idx + 1]
end
if item['locked'] == '1' then
  redis.replicate_commands()
  local time = redis.call('TIME')
  redis.call('HSET', KEYS[1], 'lockAge', time[1] - item['lockDate'])
  return { '0', item['lockCookie'], '1', nil, nil }
else
  redis.call('EXPIRE', KEYS[1], item['ttl'])
  return { item['lockAge'], item['lockCookie'], item['locked'], item['sessionItem'], item['uninitialized'] }
end";

        private static readonly string GetStateItemExclusive = @"
if redis.call('EXISTS', KEYS[1]) == 0 then
  return nil
end
local result = redis.call('HGETALL', KEYS[1])
local item = {}
for idx = 1, #result, 2 do
    item[result[idx]] = result[idx + 1]
end
if item['locked'] == '1' then
  redis.replicate_commands()
  local time = redis.call('TIME')
  redis.call('HSET', KEYS[1], 'lockAge', time[1] - item['lockDate'])
  return { '0', item['lockCookie'], '1', nil, nil }
else
  redis.call('HMSET', KEYS[1], 'lockAge', 0, 'lockCookie', item['lockCookie'] + 1, 'locked', '1')
  redis.call('EXPIRE', KEYS[1], item['ttl'])
  return { item['lockAge'], item['lockCookie'] + 1, '0', item['sessionItem'], item['uninitialized'] }
end";

        private static readonly string ReleaseItemExclusive = @"
if redis.call('EXISTS', KEYS[1]) == 0 then
  return nil
end
local lockCookie = redis.call('HGET', KEYS[1], 'lockCookie')
if lockCookie == ARGV[1] then
  redis.call('HMSET', KEYS[1], 'locked', '0', 'uninitialized', '0')
  redis.call('EXPIRE', KEYS[1], redis.call('HGET', KEYS[1], 'ttl'))
  return 1
else
  return 0
end";

        private static readonly string RemoveStateItem = @"
if redis.call('EXISTS', KEYS[1]) == 0 then
  return nil
end
local lockCookie = redis.call('HGET', KEYS[1], 'lockCookie')
if lockCookie == ARGV[1] then
  redis.call('DEL', KEYS[1])
  return 1
else
  return 0
end";

        private static readonly string ResetItemTimeout = @"
local ttl = redis.call('HGET', KEYS[1], 'ttl')
if ttl == nil then
  return 1
else
  redis.call('EXPIRE', KEYS[1], ttl)
end";

        private static readonly string UpdateSessionStateItem = @"
if redis.call('EXISTS', KEYS[1]) == 0 then
  return nil
end
local result = redis.call('HGETALL', KEYS[1])
local item = {}
for idx = 1, #result, 2 do
    item[result[idx]] = result[idx + 1]
end
if item['lockCookie'] == ARGV[1] then
  redis.call('HMSET', KEYS[1], 'ttl', ARGV[2], 'sessionItem', ARGV[3], 'locked', '0')
  redis.call('EXPIRE', KEYS[1], ARGV[2])
  return 1
else
  return 0
end";

        #endregion

        /// <summary>
        /// Initialize the provider through the configuration
        /// </summary>
        /// <param name="name">Sessionstate provider name</param>
        /// <param name="config">Configuration values</param>
        public override void Initialize(string name, NameValueCollection config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (string.IsNullOrEmpty(name))
            {
                name = "RedisSessionStateProviderAsync";
            }

            var ssc = (SessionStateSection)WebConfigurationManager.GetSection("system.web/sessionState");
            var connectionString = GetConnectionString(config["connectionStringName"]);

            Initialize(name, config, ssc, connectionString);
        }

        internal void Initialize(string name, NameValueCollection providerConfig, SessionStateSection ssc, ConnectionStringSettings connectionString)
        {
            base.Initialize(name, providerConfig);

            if (!s_oneTimeInited)
            {
                lock (s_lock)
                {
                    if (!s_oneTimeInited)
                    {
                        s_compressionEnabled = ssc.CompressionEnabled;
                        s_timeout = (int)ssc.Timeout.TotalSeconds;

                        if (int.TryParse(providerConfig["databaseId"], out s_databaseId))
                        {
                            s_databaseId = -1;
                        }

                        s_connectionString = connectionString.ConnectionString;

                        s_multiplexer = ConnectionMultiplexer.Connect(ConfigurationOptions.Parse(s_connectionString));
                        s_connection = s_multiplexer.GetDatabase(s_databaseId);

                        s_oneTimeInited = true;
                    }
                }
            }
        }

        #region properties/methods for unit tests

        internal bool CompressionEnabled
        {
            get { return s_compressionEnabled; }
        }

        internal int Timeout
        {
            get { return s_timeout; }
        }

        internal int DatabaseId
        {
            get { return s_databaseId; }
        }

        internal string ConnectionString
        {
            get { return s_connectionString; }
        }

        internal static Func<HttpContext, HttpStaticObjectsCollection> GetSessionStaticObjects
        {
            get; set;
        } = SessionStateUtility.GetSessionStaticObjects;

        #endregion

        /// <inheritdoc />
        public override SessionStateStoreData CreateNewStoreData(HttpContextBase context, int timeout)
        {
            var staticObjects = context != null ? GetSessionStaticObjects(context.ApplicationInstance.Context) : null;

            return new SessionStateStoreData(new SessionStateItemCollection(), staticObjects, timeout);
        }

        /// <inheritdoc />
        public override async Task CreateUninitializedItemAsync(HttpContextBase context, string id, int timeout, CancellationToken cancellationToken)
        {
            if (id == null)
            {
                throw new ArgumentNullException(nameof(id));
            }

            var item = new SessionStateStoreData(new SessionStateItemCollection(), GetSessionStaticObjects(context.ApplicationInstance.Context), timeout);

            SerializeStoreData(item, out var buf, s_compressionEnabled);

            var timeoutInSecs = 60 * timeout;
            await CreateSessionStateItemAsync(id, timeoutInSecs, buf, true);
        }

        /// <inheritdoc />
        public override void Dispose()
        {
        }

        /// <inheritdoc />
        public override bool SetItemExpireCallback(SessionStateItemExpireCallback expireCallback)
        {
            return false;
        }

        /// <inheritdoc />
        public override async Task SetAndReleaseItemExclusiveAsync(HttpContextBase context, string id, SessionStateStoreData item, object lockId, bool newItem, CancellationToken cancellationToken)
        {
            byte[] buf;

            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            if (id == null)
            {
                throw new ArgumentNullException(nameof(id));
            }

            try
            {
                SerializeStoreData(item, out buf, s_compressionEnabled);
            }
            catch
            {
                if (!newItem)
                {
                    await ReleaseItemExclusiveAsync(context, id, lockId, cancellationToken);
                }

                throw;
            }

            var lockCookie = (int?)lockId ?? DefaultLockCookie;

            if (newItem)
            {
                await CreateSessionStateItemAsync(id, s_timeout, buf, false);
            }
            else
            {
                await s_connection.ScriptEvaluateAsync(UpdateSessionStateItem, new RedisKey[] { id }, new RedisValue[] { lockCookie, 60 * item.Timeout, buf });
            }
        }

        /// <inheritdoc />
        public override async Task ResetItemTimeoutAsync(HttpContextBase context, string id, CancellationToken cancellationToken)
        {
            await s_connection.ScriptEvaluateAsync(ResetItemTimeout, new RedisKey[] { id });
        }

        /// <inheritdoc />
        public override async Task RemoveItemAsync(HttpContextBase context, string id, object lockId, SessionStateStoreData item, CancellationToken cancellationToken)
        {
            await s_connection.ScriptEvaluateAsync(RemoveStateItem, new RedisKey[] { id }, new RedisValue[] { (int)lockId });
        }

        /// <inheritdoc />
        public override async Task ReleaseItemExclusiveAsync(HttpContextBase context, string id, object lockId, CancellationToken cancellationToken)
        {
            await s_connection.ScriptEvaluateAsync(ReleaseItemExclusive, new RedisKey[] { id }, new RedisValue[] { (int)lockId });
        }

        /// <inheritdoc />
        public override void InitializeRequest(HttpContextBase context)
        {
        }

        /// <inheritdoc />
        public override Task<GetItemResult> GetItemExclusiveAsync(HttpContextBase context, string id, CancellationToken cancellationToken)
        {
            return DoGet(context, id, true);
        }

        /// <inheritdoc />
        public override Task<GetItemResult> GetItemAsync(HttpContextBase context, string id, CancellationToken cancellationToken)
        {
            return DoGet(context, id, false);
        }

        /// <inheritdoc />
        public override Task EndRequestAsync(HttpContextBase context)
        {
            return Task.CompletedTask;
        }

        private async Task<GetItemResult> DoGet(HttpContextBase context, string id, bool exclusive)
        {
            var script = exclusive ? GetStateItemExclusive : GetStateItem;

            var result = await s_connection.ScriptEvaluateAsync(script, new RedisKey[] { id });

            if (result.IsNull)
            {
                return null;
            }

            var item = (RedisResult[])result;

            var sessionStateItem = new SessionStateItem
            {
                LockAge = item[0].IsNull ? (TimeSpan?)null : TimeSpan.FromSeconds((int)item[0]),
                LockCookie = (int?)item[1],
                Locked = (bool?)item[2],
                SessionItem = item.Length > 3 ? (byte[])item[3] : null,
                Uninitialized = item.Length > 4 ? (bool?)item[4] : null
            };

            if (sessionStateItem.Locked.Value)
            {
                return new GetItemResult(null, sessionStateItem.Locked.Value, sessionStateItem.LockAge.Value, sessionStateItem.LockCookie.Value, SessionStateActions.None);
            }
            else
            {
                using (var stream = new MemoryStream(sessionStateItem.SessionItem))
                {
                    var data = DeserializeStoreData(context, stream, s_compressionEnabled);
                    var action = (sessionStateItem.Uninitialized ?? false) ? SessionStateActions.InitializeItem : SessionStateActions.None;
                    return new GetItemResult(data, sessionStateItem.Locked.Value, sessionStateItem.LockAge.Value, sessionStateItem.LockCookie.Value, action);
                }
            }
        }

        internal async Task CreateSessionStateItemAsync(string sessionid, int timeoutInSec, byte[] ssItems, bool uninitialized)
        {
            await s_connection.ScriptEvaluateAsync(CreateSessionStateItem, new RedisKey[] { sessionid },
                new RedisValue[] { timeoutInSec, DefaultLockCookie, ssItems, uninitialized });
        }

        // Internal code copied from SessionStateUtility
        internal static void SerializeStoreData(SessionStateStoreData item, out byte[] buf, bool compressionEnabled)
        {
            using (MemoryStream s = new MemoryStream())
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

        internal static SessionStateStoreData DeserializeStoreData(HttpContextBase context, Stream stream, bool compressionEnabled)
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
                else
                {
                    sessionItems = new SessionStateItemCollection();
                }

                if (hasStaticObjects)
                {
                    staticObjects = HttpStaticObjectsCollection.Deserialize(reader);
                }
                else
                {
                    staticObjects = GetSessionStaticObjects(context.ApplicationInstance.Context);
                }

                eof = reader.ReadByte();
                if (eof != 0xff)
                {
                    throw new HttpException(String.Format(CultureInfo.CurrentCulture, Resources.SR.Invalid_session_state));
                }
            }
            catch (EndOfStreamException)
            {
                throw new HttpException(String.Format(CultureInfo.CurrentCulture, Resources.SR.Invalid_session_state));
            }

            return new SessionStateStoreData(sessionItems, staticObjects, timeout);
        }

        private static ConnectionStringSettings GetConnectionString(string connectionstringName)
        {
            if (string.IsNullOrEmpty(connectionstringName))
            {
                throw new ProviderException(Resources.SR.Connection_name_not_specified);
            }
            ConnectionStringSettings conn = ConfigurationManager.ConnectionStrings[connectionstringName];
            if (conn == null)
            {
                throw new ProviderException(
                    String.Format(CultureInfo.CurrentCulture, Resources.SR.Connection_string_not_found, connectionstringName));
            }
            return conn;
        }
    }
}
