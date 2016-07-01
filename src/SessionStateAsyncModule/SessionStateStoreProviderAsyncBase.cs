using System;
using System.Configuration.Provider;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.SessionState;

namespace Microsoft.AspNet.SessionState
{
    public sealed class GetItemResult
    {
        public GetItemResult(SessionStateStoreData item, bool locked, TimeSpan lockAge, object lockId,
            SessionStateActions actions)
        {
            Item = item;
            Locked = locked;
            LockAge = lockAge;
            LockId = lockId;
            Actions = actions;
        }

        public SessionStateStoreData Item { get; private set; }
        public bool Locked { get; private set; }
        public TimeSpan LockAge { get; private set; }
        public object LockId { get; private set; }
        public SessionStateActions Actions { get; private set; }
    }

    public abstract class SessionStateStoreProviderAsyncBase : ProviderBase
    {
        public abstract SessionStateStoreData CreateNewStoreData(HttpContextBase context, int timeout);

        public abstract Task CreateUninitializedItemAsync(HttpContextBase context, string id, int timeout,
            CancellationToken cancellationToken);

        public abstract void Dispose();
        public abstract Task EndRequestAsync(HttpContextBase context);

        public abstract Task<GetItemResult> GetItemAsync(HttpContextBase context, string id,
            CancellationToken cancellationToken);

        public abstract Task<GetItemResult> GetItemExclusiveAsync(HttpContextBase context, string id,
            CancellationToken cancellationToken);

        public abstract void InitializeRequest(HttpContextBase context);

        public abstract Task ReleaseItemExclusiveAsync(HttpContextBase context, string id, object lockId,
            CancellationToken cancellationToken);

        public abstract Task RemoveItemAsync(HttpContextBase context, string id, object lockId, SessionStateStoreData item,
            CancellationToken cancellationToken);

        public abstract Task ResetItemTimeoutAsync(HttpContextBase context, string id, CancellationToken cancellationToken);

        public abstract Task SetAndReleaseItemExclusiveAsync(HttpContextBase context, string id, SessionStateStoreData item,
            object lockId, bool newItem, CancellationToken cancellationToken);

        public abstract bool SetItemExpireCallback(SessionStateItemExpireCallback expireCallback);
    }
}