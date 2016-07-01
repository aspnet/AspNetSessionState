using System;
using System.Linq;
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
using Microsoft.AspNet.SessionState.AsyncProviders.SqlSessionState.Entities;
using Microsoft.AspNet.SessionState.AsyncProviders.SqlSessionState.Resources;
using System.Collections.Specialized;
using System.Data.Entity.Infrastructure;

namespace Microsoft.AspNet.SessionState.AsyncProviders
{
    public class SqlSessionStateAsyncProvider : SessionStateStoreProviderAsyncBase
    {
        private const int ItemShortLength = 7000;
        private const double SessionExpiresFrequencyCheckInSeconds = 30.0;
        private static long s_lastSessionPurgeTicks;
        private static bool s_inPurge = false;
        private static readonly Task s_completedTask = Task.FromResult<object>(null);
        private static string s_appSuffix = null;

        private ConnectionStringSettings ConnectionString { get; set; }
        private bool CompressionEnabled { get; set; }
        internal long LastSessionPurgeTicks
        {
            get
            {
                return Interlocked.Read(ref s_lastSessionPurgeTicks);
            }
            set
            {
                Interlocked.Exchange(ref s_lastSessionPurgeTicks, value);
            }
        }

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

            ConnectionString = ModelHelper.GetConnectionString(config["connectionStringName"]);
            config.Remove("connectionStringName");

            try
            {
                SessionStateSection ssc = (SessionStateSection)ConfigurationManager.GetSection("system.web/sessionState");
                CompressionEnabled = ssc.CompressionEnabled;
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

        public override SessionStateStoreData CreateNewStoreData(HttpContextBase context, int timeout)
        {
            HttpStaticObjectsCollection staticObjects = null;
            if (context != null)
            {
                staticObjects = SessionStateUtility.GetSessionStaticObjects(context.ApplicationInstance.Context);
            }

            return new SessionStateStoreData(new SessionStateItemCollection(), staticObjects, timeout);
        }

        public override async Task CreateUninitializedItemAsync(HttpContextBase context, string id, int timeout, CancellationToken cancellationToken)
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

            using (SessionContext db = ModelHelper.CreateSessionContext(ConnectionString))
            {
                var session = await db.Sessions.FindAsync(cancellationToken, id);
                if (session == null)
                {
                    Session s = NewSession(id, timeout);
                    SessionStateStoreData item = new SessionStateStoreData(new SessionStateItemCollection(),
                        SessionStateUtility.GetSessionStaticObjects(context.ApplicationInstance.Context),
                        s.Timeout);
                    SaveItemToSession(s, item, CompressionEnabled);
                    db.Sessions.Add(s);
                    try
                    {
                        await db.SaveChangesAsync(cancellationToken);
                    }
                    // in case of concurrent insert
                    catch (DbUpdateException) { }
                }
            }
        }

        public override void Dispose()
        {
        }

        public override Task EndRequestAsync(HttpContextBase context)
        {
            PurgeIfNeeded();
            return s_completedTask;
        }

        public override Task<GetItemResult> GetItemAsync(HttpContextBase context, string id, CancellationToken cancellationToken)
        {
            return DoGet(context, id, false, cancellationToken);
        }

        public override Task<GetItemResult> GetItemExclusiveAsync(HttpContextBase context, string id, CancellationToken cancellationToken)
        {
            return DoGet(context, id, true, cancellationToken);
        }

        public override void InitializeRequest(HttpContextBase context)
        {
        }

        public override async Task ReleaseItemExclusiveAsync(HttpContextBase context, string id, object lockId, CancellationToken cancellationToken)
        {
            if (id == null)
            {
                throw new ArgumentNullException("id");
            }
            if (id.Length > SessionIDManager.SessionIDMaxLength)
            {
                throw new ArgumentException(SR.Session_id_too_long);
            }

            using (SessionContext db = ModelHelper.CreateSessionContext(ConnectionString))
            {
                id = AppendAppIdHash(id);
                await ReleaseItemNoSave(db, id, lockId, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        public override async Task RemoveItemAsync(HttpContextBase context, string id, object lockId, SessionStateStoreData item, CancellationToken cancellationToken)
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
            using (SessionContext db = ModelHelper.CreateSessionContext(ConnectionString))
            {
                Session session = await db.Sessions.FindAsync(cancellationToken, id);
                if (session != null && session.LockCookie == (int)lockId)
                {
                    db.Sessions.Remove(session);
                    await db.SaveChangesAsync(cancellationToken);
                }
            }
        }

        public override async Task ResetItemTimeoutAsync(System.Web.HttpContextBase context, string id, CancellationToken cancellationToken)
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
            using (SessionContext db = ModelHelper.CreateSessionContext(ConnectionString))
            {
                Session session = await db.Sessions.FindAsync(cancellationToken, id);
                if (session != null)
                {
                    session.Expires = DateTime.UtcNow.AddMinutes(session.Timeout);
                    await db.SaveChangesAsync(cancellationToken);
                }
                else {
                    // REVIEW: what to do if no matching session found?
                }
            }
        }

        public override async Task SetAndReleaseItemExclusiveAsync(HttpContextBase context, string id, SessionStateStoreData item, object lockId, bool newItem, CancellationToken cancellationToken)
        {
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

            using (SessionContext db = ModelHelper.CreateSessionContext(ConnectionString))
            {
                Session session = await db.Sessions.FindAsync(cancellationToken, id);
                if (session == null)
                {
                    if (newItem)
                    {
                        // NOTE: The session will already exist if its expired, so we don't need to recreate
                        session = NewSession(id, item.Timeout);
                        db.Sessions.Add(session);
                    }
                    else {
                        if (session == null)
                        {
                            throw new InvalidOperationException(SR.Session_not_found);
                        }
                    }
                }
                else {
                    if (lockId == null)
                    {
                        session.LockCookie = 0;
                    }
                    else {
                        session.LockCookie = (int)lockId;
                    }
                    session.Locked = false;
                    session.Timeout = item.Timeout;
                }

                SaveItemToSession(session, item, CompressionEnabled);

                // set item
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        public override bool SetItemExpireCallback(System.Web.SessionState.SessionStateItemExpireCallback expireCallback)
        {
            return false;
        }

        // We just want to append an 8 char hash from the AppDomainAppId to prevent any session id collisions (this is what SQL Session does)
        private static string AppendAppIdHash(string id)
        {
            if (!id.EndsWith(s_appSuffix))
            {
                return id + s_appSuffix;
            }
            return id;
        }

        private static Session NewSession(string id, int timeout)
        {
            Session s = new Session();
            DateTime now = DateTime.UtcNow;
            s.Created = now;
            s.SessionId = id;
            s.Timeout = timeout;
            s.Expires = now.AddMinutes(timeout);
            s.Locked = false;
            s.LockDate = now;
            s.LockCookie = 0;
            s.Flags = (int)SessionStateActions.None;
            return s;
        }

        private static void SaveItemToSession(Session session, SessionStateStoreData item, bool compression)
        {
            byte[] buf = null;
            int length = 0;
            SerializeStoreData(item, ItemShortLength, out buf, out length, compression);
            session.SessionItem = buf;
        }

        // Internal code copied from SessionStateUtility
        private static void SerializeStoreData(SessionStateStoreData item, int initialStreamSize, out byte[] buf, out int length, bool compressionEnabled)
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

        private bool CanPurge()
        {
            return (!s_inPurge &&
                TimeSpan.FromTicks(DateTime.UtcNow.Ticks - LastSessionPurgeTicks).TotalSeconds > SessionExpiresFrequencyCheckInSeconds);
        }

        private void PurgeIfNeeded()
        {
            // Only check for expired sessions every 30 seconds.
            if (CanPurge())
            {
                Task purgeTask = new Task(() => PurgeExpiredSessions());
                purgeTask.Start();
            }
        }

        private void PurgeExpiredSessions()
        {
            try
            {
                s_inPurge = true;
                // Note: EF will throw Concurrency exceptions if multiple threads try to delete the object, so this should be safe
                using (SessionContext db = ModelHelper.CreateSessionContext(ConnectionString))
                {
                    var now = DateTime.UtcNow;
                    var expired = from s in db.Sessions
                                  where s.Expires < now
                                  select s;
                    db.Sessions.RemoveRange(expired);
                    db.SaveChanges();
                    LastSessionPurgeTicks = now.Ticks;
                }
            }
            catch
            {
                // Swallow all failures, this is called from an async Task and we don't want to crash
            }
            finally
            {
                s_inPurge = false;
            }
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
            object lockId = null;
            SessionStateActions actions = SessionStateActions.None;
            var now = DateTime.UtcNow;

            using (SessionContext db = ModelHelper.CreateSessionContext(ConnectionString))
            {
                Session session = await db.Sessions.FindAsync(cancellationToken, id);
                if (session != null && session.Expires > now)
                {
                    session.Expires = now.AddMinutes(session.Timeout);
                    locked = session.Locked;
                    lockId = session.LockCookie;
                    //Trace.WriteLine(string.Format("{4} before id:{0} locked:{1} lockDate:{2} lockId:{3}", id, locked, session.LockDate, lockId, Thread.CurrentThread.ManagedThreadId));

                    SessionStateStoreData item = null;
                    if (locked)
                    {
                        lockAge = TimeSpan.FromSeconds((now - session.LockDate).Seconds);
                    }
                    else {
                        // not locked
                        if (exclusive)
                        {
                            session.Locked = true;
                            session.LockDate = now;
                        }
                    }
                    try
                    {
                        await db.SaveChangesAsync(cancellationToken);
                    }
                    // whenever we see optimistic concurrency in locking item, treat it as locked
                    catch (DbUpdateConcurrencyException)
                    {
                        return new GetItemResult(null, true, lockAge, lockId, actions); ;
                    }

                    if (!locked)
                    {
                        byte[] buf = session.SessionItem;
                        if ((SessionStateActions)session.Flags == SessionStateActions.InitializeItem)
                        {
                            item = InitializeSessionItem(context, session, CompressionEnabled);
                        }
                        else
                        {
                            using (MemoryStream s = new MemoryStream(buf))
                            {
                                item = DeserializeStoreData(context, s, CompressionEnabled);
                            }
                        }
                    }
                    return new GetItemResult(item, locked, lockAge, lockId, actions);
                }
            }
            return null;
        }

        private static SessionStateStoreData InitializeSessionItem(HttpContextBase context, Session session, bool compression)
        {
            SessionStateStoreData item = new SessionStateStoreData(new SessionStateItemCollection(),
                SessionStateUtility.GetSessionStaticObjects(context.ApplicationInstance.Context),
                session.Timeout);
            SaveItemToSession(session, item, compression);
            session.Flags = (int)SessionStateActions.None;
            return item;
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

        private static async Task ReleaseItemNoSave(SessionContext db, string id, object lockId, CancellationToken cancellationToken)
        {
            // only unlock if we have the lock
            Session session = await db.Sessions.FindAsync(cancellationToken, id);
            if (session != null && session.Locked && session.LockCookie == (int)lockId)
            {
                session.Locked = false;
            }
        }
    }
}
