// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See the License.txt file in the project root for full license information.

namespace Microsoft.AspNet.SessionState
{
    using Microsoft.AspNet.SessionState.Resources;
    using System;
    using System.Collections.Specialized;
    using System.Configuration;
    using System.Configuration.Provider;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web;
    using System.Web.SessionState;

    /// <summary>
    /// The retrieved result from the sessionstate data store
    /// </summary>
    public sealed class GetItemResult
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="item">SessionState data</param>
        /// <param name="locked">Whether the session is locked or not</param>
        /// <param name="lockAge">How long the session is locked</param>
        /// <param name="lockId">Lock ID</param>
        /// <param name="actions">SessionState action</param>
        public GetItemResult(SessionStateStoreData item, bool locked, TimeSpan lockAge, object lockId,
            SessionStateActions actions)
        {
            Item = item;
            Locked = locked;
            LockAge = lockAge;
            LockId = lockId;
            Actions = actions;
        }

        /// <summary>
        /// SessionState store data
        /// </summary>
        public SessionStateStoreData Item { get; private set; }

        /// <summary>
        /// Gets or sets whether the session item is locked or not
        /// </summary>
        public bool Locked { get; private set; }

        /// <summary>
        /// Gets or sets the duration for which session item is locked
        /// </summary>
        public TimeSpan LockAge { get; private set; }

        /// <summary>
        /// Gets or sets the lock context
        /// </summary>
        public object LockId { get; private set; }

        /// <summary>
        /// Gets or sets session state action
        /// </summary>
        public SessionStateActions Actions { get; private set; }
    }

    /// <summary>
    /// The base class of async version sessionstate provider
    /// </summary>
    public abstract class SessionStateStoreProviderAsyncBase : ProviderBase
    {
        /// <summary>
        /// Creates a new SessionStateStoreData object to be used for the current request.
        /// </summary>
        /// <param name="context">The HttpContext for the current request</param>
        /// <param name="timeout">The session state timeout value for the new SessionStateStoreData</param>
        /// <returns></returns>
        public abstract SessionStateStoreData CreateNewStoreData(HttpContextBase context, int timeout);

        /// <summary>
        /// Create uninitialized session item
        /// </summary>
        /// <param name="context">HttpContext</param>
        /// <param name="id">Session ID</param>
        /// <param name="timeout">The session state timeout value</param>
        /// <param name="cancellationToken">Cancellation token for the async task</param>
        /// <returns></returns>
        public abstract Task CreateUninitializedItemAsync(
            HttpContextBase context, 
            string id, 
            int timeout,
            CancellationToken cancellationToken);

        /// <summary>
        /// Dispose resource
        /// </summary>
        public abstract void Dispose();

        /// <summary>
        /// Async callback for EndRequest event
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public abstract Task EndRequestAsync(HttpContextBase context);

        /// <summary>
        /// Retrieve session item without lock
        /// </summary>
        /// <param name="context">HttpContext</param>
        /// <param name="id">Session ID</param>
        /// <param name="cancellationToken">Cancellation token for the async task</param>
        /// <returns>A task that retrieves the session item without lock</returns>
        public abstract Task<GetItemResult> GetItemAsync(
            HttpContextBase context, 
            string id,
            CancellationToken cancellationToken);

        /// <summary>
        /// Retrieve sessionitem with lock
        /// </summary>
        /// <param name="context">HttpContext</param>
        /// <param name="id">Session ID</param>
        /// <param name="cancellationToken">Cancellation token for the async task</param>
        /// <returns>A task that retrieves the session item with lock</returns>
        public abstract Task<GetItemResult> GetItemExclusiveAsync(
            HttpContextBase context, 
            string id,
            CancellationToken cancellationToken);

        /// <summary>
        /// Called at the beginning of the AcquireRequestState event
        /// </summary>
        /// <param name="context">The HttpContext for the current request</param>
        public abstract void InitializeRequest(HttpContextBase context);

        /// <summary>
        /// Unlock an item locked by GetExclusive
        /// </summary>
        /// <param name="context">The HttpContext for the current request</param>
        /// <param name="id">Session ID</param>
        /// <param name="lockId">Session item lock context</param>
        /// <param name="cancellationToken">Cancellation token for the async task</param>
        /// <returns></returns>
        public abstract Task ReleaseItemExclusiveAsync(
            HttpContextBase context, 
            string id, 
            object lockId,
            CancellationToken cancellationToken);

        /// <summary>
        /// Remove the session item from the store
        /// </summary>
        /// <param name="context">The HttpContext for the current request</param>
        /// <param name="id">Session ID</param>
        /// <param name="lockId">Session item lock context</param>
        /// <param name="item">Session data</param>
        /// <param name="cancellationToken">Cancellation token for the async task</param>
        /// <returns></returns>
        public abstract Task RemoveItemAsync(
            HttpContextBase context, 
            string id, 
            object lockId, 
            SessionStateStoreData item,
            CancellationToken cancellationToken);

        /// <summary>
        /// Reset the expire time of an item based on its timeout value
        /// </summary>
        /// <param name="context">The HttpContext for the current request</param>
        /// <param name="id">Session ID</param>
        /// <param name="cancellationToken">Cancellation token for the async task</param>
        /// <returns></returns>
        public abstract Task ResetItemTimeoutAsync(HttpContextBase context, string id, CancellationToken cancellationToken);

        /// <summary>
        /// Updates the session-item information in the session-state data store with values from the current request, 
        /// and clears the lock on the data
        /// </summary>
        /// <param name="context">The HttpContext for the current request</param>
        /// <param name="id">Session ID</param>
        /// <param name="item">Session data</param>
        /// <param name="lockId">Session item lock context</param>
        /// <param name="newItem">Whether it is a new session item</param>
        /// <param name="cancellationToken">Cancellation token for the async task</param>
        /// <returns></returns>
        public abstract Task SetAndReleaseItemExclusiveAsync(
            HttpContextBase context, 
            string id, 
            SessionStateStoreData item,
            object lockId, 
            bool newItem, 
            CancellationToken cancellationToken);

        /// <summary>
        /// Sets a reference to the SessionStateItemExpireCallback delegate for the Session_OnEnd event
        /// </summary>
        /// <param name="expireCallback"></param>
        /// <returns></returns>
        public abstract bool SetItemExpireCallback(SessionStateItemExpireCallback expireCallback);

        /// <inheritdoc />
        public override void Initialize(string name, NameValueCollection config)
        {
            base.Initialize(name, config);

            // This is really a module-level setting, but it can't be specified within the rigid typed xml structure of the session-state module.
            // So we have inserted this setting here and it will apply to all providers that this async module can use.

            // skipKeepAliveWhenUnused
            var skipKA = config["skipKeepAliveWhenUnused"];
            if (skipKA != null && !bool.TryParse(skipKA, out _skipKeepAliveWhenUnused))
            {
                throw new ConfigurationErrorsException(string.Format(SR.Invalid_provider_option, "skipKeepAliveWhenUnused", name));
            }
        }

        private bool _skipKeepAliveWhenUnused = false;
        internal bool SkipKeepAliveWhenUnused { get { return _skipKeepAliveWhenUnused; } }
    }
}