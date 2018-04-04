// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See the License.txt file in the project root for full license information.

namespace Microsoft.AspNet.SessionState
{
    using System;
    using System.Collections.Specialized;
    using System.Diagnostics;
    using System.Runtime.Caching;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web;
    using System.Web.SessionState;
    using Resources;

    /// <summary>
    /// Default in-memory SessionState provider for async SessionState module
    /// </summary>
    public sealed class InProcSessionStateStoreAsync : SessionStateStoreProviderAsyncBase
    {
        private const int NewLockCookie = 1;
        private static readonly MemoryCache s_store = new MemoryCache("InProcSessionStateStoreAsync");

        private CacheEntryRemovedCallback _callback;
        private SessionStateItemExpireCallback _expireCallback;
        
        /// <inheritdoc />
        public override void Initialize(string name, NameValueCollection config)
        {
            if(string.IsNullOrEmpty(name))
            {
                name = "InProc async session state provider";
            }
            base.Initialize(name, config);

            _callback = new CacheEntryRemovedCallback(OnCacheItemRemoved);
        }

        /// <inheritdoc />
        public override SessionStateStoreData CreateNewStoreData(HttpContextBase context, int timeout)
        {
            return CreateLegitStoreData(context, null, null, timeout);
        }

        /// <inheritdoc />
        public override Task CreateUninitializedItemAsync(
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

            var state = new InProcSessionState(
                    null,
                    null,
                    timeout,
                    false,
                    DateTime.MinValue,
                    NewLockCookie,
                    (int)SessionStateItemFlags.Uninitialized);
                       
            InsertToCache(id, state);

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override void Dispose()
        {
        }

        /// <inheritdoc />
        public override Task EndRequestAsync(HttpContextBase context)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override Task<GetItemResult> GetItemAsync(HttpContextBase context, string id, CancellationToken cancellationToken)
        {
            return DoGetAsync(context, id, false);
        }       

        /// <inheritdoc />
        public override Task<GetItemResult> GetItemExclusiveAsync(HttpContextBase context, string id, 
            CancellationToken cancellationToken)
        {
            return DoGetAsync(context, id, true);
        }

        /// <inheritdoc />
        public override void InitializeRequest(HttpContextBase context)
        {
        }

        /// <inheritdoc />
        public override Task ReleaseItemExclusiveAsync(
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

            var lockCookie = (int)lockId;
            var state = (InProcSessionState)s_store.Get(id);

            if(state != null && state.Locked)
            {
                bool lockTaken = false;
                try
                {
                    state.SpinLock.Enter(ref lockTaken);

                    if(state.Locked && lockCookie == state.LockCookie)
                    {
                        state.Locked = false;
                    }
                }
                finally
                {
                    if(lockTaken)
                    {
                        state.SpinLock.Exit();
                    }
                }
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override Task RemoveItemAsync(
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

            s_store.Remove(id);

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override Task ResetItemTimeoutAsync(HttpContextBase context, string id, CancellationToken cancellationToken)
        {
            s_store.Get(id);

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override Task SetAndReleaseItemExclusiveAsync(
            HttpContextBase context, 
            string id, 
            SessionStateStoreData item, 
            object lockId, 
            bool newItem, 
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

            Debug.Assert(item != null, "item != null");
            Debug.Assert(item.StaticObjects != null, "item.StaticObjects != null");

            ISessionStateItemCollection items = null;
            HttpStaticObjectsCollection staticObjects = null;
            var doInsert = true;
            var lockCookieForInsert = NewLockCookie;

            if (item.Items.Count > 0)
            {
                items = item.Items;
            }

            if(!item.StaticObjects.NeverAccessed)
            {
                staticObjects = item.StaticObjects;
            }

            if(!newItem)
            {
                var currentState = (InProcSessionState)s_store.Get(id);
                var lockCookie = (int)lockId;

                if(currentState == null)
                {
                    return Task.CompletedTask;
                }

                var lockTaken = false;
                try
                {
                    currentState.SpinLock.Enter(ref lockTaken);

                    // we can change the state in place if the timeout hasn't changed
                    if(currentState.Timeout == item.Timeout)
                    {
                        currentState.Copy(items, staticObjects, item.Timeout, false, DateTime.MinValue, lockCookie, currentState.Flags);

                        doInsert = false;
                    }
                    else
                    {
                        /* We are going to insert a new item to replace the current one in Cache
                           because the expiry time has changed.
                           
                           Pleas note that an insert will cause the Session_End to be incorrectly raised. 
                           
                           Please note that the item itself should not expire between now and
                           where we do MemoryCache.Insert below because MemoryCache.Get above have just
                           updated its expiry time.
                        */
                        currentState.Flags |= (int)SessionStateItemFlags.IgnoreCacheItemRemoved;
                        lockCookieForInsert = lockCookie;
                    }
                }
                finally
                {
                    if(lockTaken)
                    {
                        currentState.SpinLock.Exit();
                    }
                }
            }

            if (doInsert)
            {
                var newState = new InProcSessionState(
                    items,
                    staticObjects,
                    item.Timeout,
                    false,
                    DateTime.MinValue,
                    lockCookieForInsert,
                    0);

                InsertToCache(id, newState);
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override bool SetItemExpireCallback(SessionStateItemExpireCallback expireCallback)
        {
            _expireCallback = expireCallback;
            return true;
        }

        private void OnCacheItemRemoved(CacheEntryRemovedArguments arguments)
        {
            var state = (InProcSessionState)arguments.CacheItem.Value;

            if((state.Flags & (int)SessionStateItemFlags.IgnoreCacheItemRemoved) != 0 ||
                (state.Flags & (int)SessionStateItemFlags.Uninitialized) != 0)
            {
                return;
            }

            if(_expireCallback != null)
            {
                var item = CreateLegitStoreData(null, state.SessionItems, state.StaticObjects, state.Timeout);
                _expireCallback(arguments.CacheItem.Key, item);
            }
        }

        private Task<GetItemResult> DoGetAsync(HttpContextBase context, string id, bool exclusive)
        {
            bool locked;
            TimeSpan lockAge;
            object lockId;
            SessionStateActions actionFlags;

            var item = DoGet(context, id, exclusive, out locked, out lockAge, out lockId, out actionFlags);
            GetItemResult result = new GetItemResult(item, locked, lockAge, lockId, actionFlags);

            return Task.FromResult<GetItemResult>(result);
        }

        private SessionStateStoreData DoGet(HttpContextBase context,
                                        String id,
                                        bool exclusive,
                                        out bool locked,
                                        out TimeSpan lockAge,
                                        out object lockId,
                                        out SessionStateActions actionFlags)
        {
            // Set default return values
            locked = false;
            lockId = null;
            lockAge = TimeSpan.Zero;
            actionFlags = 0;
                        
            InProcSessionState state = (InProcSessionState)s_store.Get(id);
            if (state != null)
            {
                bool lockedByOther;       // True if the state is locked by another session
                int initialFlags;

                initialFlags = state.Flags;
                if ((initialFlags & (int)SessionStateItemFlags.Uninitialized) != 0)
                {
                    // It is an uninitialized item.  We have to remove that flag.
                    // We only allow one request to do that.
                    // If initialFlags != return value of CompareExchange, it means another request has
                    // removed the flag.

                    if (initialFlags == Interlocked.CompareExchange(
                                            ref state.Flags,
                                            initialFlags & (~((int)SessionStateItemFlags.Uninitialized)),
                                            initialFlags))
                    {
                        actionFlags = SessionStateActions.InitializeItem;
                    }
                }

                if (exclusive)
                {
                    lockedByOther = true;

                    // If unlocked, use a spinlock to test and lock the state.
                    if (!state.Locked)
                    {
                        var lockTaken = false;
                        try
                        {
                            state.SpinLock.Enter(ref lockTaken);

                            if (!state.Locked)
                            {
                                lockedByOther = false;
                                state.Locked = true;
                                state.LockDate = DateTime.UtcNow;
                                state.LockCookie++;
                            }
                            lockId = state.LockCookie;
                        }
                        finally
                        {
                            if(lockTaken)
                            {
                                state.SpinLock.Exit();
                            }
                        }
                    }
                    else
                    {
                        // It's already locked by another request.  Return the lockCookie to caller.
                        lockId = state.LockCookie;
                    }

                }
                else
                {
                    var lockTaken = false;
                    state.SpinLock.Enter(ref lockTaken);
                    try
                    {
                        lockedByOther = state.Locked;
                        lockId = state.LockCookie;
                    }
                    finally
                    {
                        if (lockTaken)
                        {
                            state.SpinLock.Exit();
                        }
                    }
                }

                if (lockedByOther)
                {
                    // Item found, but locked
                    locked = true;
                    lockAge = DateTime.UtcNow - state.LockDate;
                    return null;
                }
                else
                {
                    return CreateLegitStoreData(context, state.SessionItems, state.StaticObjects, state.Timeout);
                }
            }

            // Not found
            return null;
        }

        private void InsertToCache(string key, InProcSessionState value)
        {
            var cachePolicy = new CacheItemPolicy()
            {
                SlidingExpiration = new TimeSpan(0, value.Timeout, 0),
                RemovedCallback = _callback,
                Priority = CacheItemPriority.NotRemovable
            };
            s_store.Set(key, value, cachePolicy);
        }

        private SessionStateStoreData CreateLegitStoreData(
            HttpContextBase context,
            ISessionStateItemCollection sessionItems, 
            HttpStaticObjectsCollection staticObjects, 
            int timeout)
        {
            if (sessionItems == null)
            {
                sessionItems = new SessionStateItemCollection();
            }

            if (staticObjects == null && context != null)
            {
                staticObjects = SessionStateUtility.GetSessionStaticObjects(context.ApplicationInstance.Context);
            }

            return new SessionStateStoreData(sessionItems, staticObjects, timeout);
        }
    }

    /// <summary>
    /// The data structure used to store a session in the memory
    /// </summary>
    public sealed class InProcSessionState
    {
        private ISessionStateItemCollection _sessionItems;
        private HttpStaticObjectsCollection _staticObjects;
        private int _timeout;

        /// <summary>
        /// Gets session state items
        /// </summary>
        public ISessionStateItemCollection SessionItems
        {
            get
            {
                return _sessionItems;
            }
        }

        /// <summary>
        /// Gets a static objects collection
        /// </summary>
        public HttpStaticObjectsCollection StaticObjects
        {
            get
            {
                return _staticObjects;
            }
        }

        /// <summary>
        /// Gets timeout of a Session
        /// </summary>
        public int Timeout
        {
            get
            {
                return _timeout;
            }
        }

        /// <summary>
        /// Gets or sets if a session is locked
        /// </summary>
        public bool Locked { get; set; }

        /// <summary>
        /// Gets or sets the lock date of a session
        /// </summary>
        public DateTime LockDate { get; set; }

        /// <summary>
        /// Gets or sets the lock id of a session
        /// </summary>
        public int LockCookie { get; set; }

        /// <summary>
        /// The locker of a session
        /// </summary>
        public SpinLock SpinLock;

        /// <summary>
        /// SessionStateItem flags
        /// </summary>
        public int Flags;                           // Can't use property in Interlocked.CompareExchange

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="sessionItems">Session state items</param>
        /// <param name="staticObjects">A static objects collection</param>
        /// <param name="timeout">Timeout of the session</param>
        /// <param name="locked">Whether the session is locked or not</param>
        /// <param name="utcLockDate">Datetime the session is locked</param>
        /// <param name="lockCookie">The lock id of the session</param>
        /// <param name="flags">SessionStateItem flags</param>
        public InProcSessionState(
                ISessionStateItemCollection sessionItems,
                HttpStaticObjectsCollection staticObjects,
                int timeout,
                bool locked,
                DateTime utcLockDate,
                int lockCookie,
                int flags)
        {
            SpinLock = new SpinLock();
            Copy(sessionItems, staticObjects, timeout, locked, utcLockDate, lockCookie, flags);
        }

        /// <summary>
        /// Copy InProcSessionState data to the instance
        /// </summary>
        /// <param name="sessionItems">Session state items</param>
        /// <param name="staticObjects">A static objects collection</param>
        /// <param name="timeout">Timeout of the session</param>
        /// <param name="locked">Whether the session is locked or not</param>
        /// <param name="utcLockDate">Datetime the session is locked</param>
        /// <param name="lockCookie">The lock id of the session</param>
        /// <param name="flags">SessionStateItem flags</param>
        public void Copy(
                ISessionStateItemCollection sessionItems,
                HttpStaticObjectsCollection staticObjects,
                int timeout,
                bool locked,
                DateTime utcLockDate,
                int lockCookie,
                int flags)
        {

            _sessionItems = sessionItems;
            _staticObjects = staticObjects;
            _timeout = timeout;
            Locked = locked;
            LockDate = utcLockDate;
            LockCookie = lockCookie;
            Flags = flags;
        }
    }

    /// <summary>
    /// The state of session state item
    /// </summary>
    [Flags]
    public enum SessionStateItemFlags : int
    {
        /// <summary>
        /// No flag
        /// </summary>
        None = 0x00000000,

        /// <summary>
        /// Unintialized session state
        /// </summary>
        Uninitialized = 0x00000001,

        /// <summary>
        /// Avoid to trigger cache item removed callback due to the sessionstate timeout change
        /// </summary>
        IgnoreCacheItemRemoved = 0x00000002
    }
}
