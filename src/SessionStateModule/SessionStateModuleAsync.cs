// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See the License.txt file in the project root for full license information.

namespace Microsoft.AspNet.SessionState
{
    using System;
    using System.Configuration;
    using System.Threading.Tasks;
    using System.Web;
    using System.Web.Configuration;
    using System.Web.SessionState;
    using System.Collections.Concurrent;
    using Resources;
    using System.Diagnostics;
    using System.Collections.Generic;
    using Win32;
    using System.Threading;

    /// <summary>
    /// Async version of SessionState module which requires .Net framework 4.6.2
    /// </summary>
    public sealed class SessionStateModuleAsync : ISessionStateModule
    {
        private static long s_lockedItemPollingInterval = 500; // in milliseconds
        private static TimeSpan s_pollingTimespan;

        private static int s_timeout;
        private static SessionStateSection s_config;
        private static TimeSpan s_configExecutionTimeout;

        private static bool s_configRegenerateExpiredSessionId;

        private static HttpCookieMode s_configCookieless;
        private static SessionStateMode s_configMode;

        private static bool s_pollIntervalRegLookedUp;
        private static readonly object PollIntervalRegLock = new object();

        private static readonly object LockObject = new object();
        private static bool s_oneTimeInit;
                
        private readonly SessionOnEndTarget _onEndTarget = new SessionOnEndTarget();

        private static ConcurrentDictionary<string, int> s_queuedRequestsNumPerSession = 
            new ConcurrentDictionary<string, int>();

        /* per request data goes in _rq* variables */
        private bool _acquireCalled;
        private ISessionIDManager _idManager;
        private bool _releaseCalled;
        private SessionStateActions _rqActionFlags;
        private bool _rqAddedCookie;
        private HttpContextBase _rqContext;
        private TimeSpan _rqExecutionTimeout;
        private string _rqId;
        private bool _rqIdNew;
        private bool _rqIsNewSession;
        private SessionStateStoreData _rqItem;

        // If the ownership change hands (e.g. this ownership
        // times out), the lockId of the item at the store will change.
        private object _rqLockId; // The id of its SessionStateItem ownership
        private bool _rqReadonly;
        private bool _rqRequiresState;
        private ISessionStateItemCollection _rqSessionItems;
        private HttpSessionStateContainer _rqSessionState;
        private bool _rqSessionStateNotFound;
        private HttpStaticObjectsCollection _rqStaticObjects;

        private bool _rqSupportSessionIdReissue;

        /* per application vars */
        private EventHandler _sessionStartEventHandler;
        private SessionStateStoreProviderAsyncBase _store;
        private bool _supportSessionExpiry;

        /// <summary>
        /// Initializes a new instance of the <see cref='Microsoft.AspNet.SessionState.SessionStateModuleAsync' />
        /// </summary>
        public SessionStateModuleAsync()
        {
        }

        /// <summary>
        /// Get the HttpCookieMode setting of the module
        /// </summary>
        public static HttpCookieMode ConfigCookieless
        {
            get
            {
                return s_configCookieless;
            }
        }
        
        /// <summary>
        /// Get the SessionStateMode setting of the module
        /// </summary>
        public static SessionStateMode ConfigMode
        {
            get
            {
                return s_configMode;
            }
        }
        /// <summary>
        /// Initialize the module
        /// </summary>
        /// <param name="app"></param>
        public void Init(HttpApplication app)
        {
            bool initModuleCalled = false;

            if (!s_oneTimeInit)
            {
                lock (LockObject)
                {
                    if (!s_oneTimeInit)
                    {
                        s_config = ConfigurationManager.GetSection("system.web/sessionState") as SessionStateSection;
                        if (s_config == null)
                        {
                            throw new ConfigurationErrorsException(string.Format(SR.Error_Occured_Reading_Config_Secion, "system.web/sessionState"));
                        }

                        InitModuleFromConfig(app, s_config);
                        initModuleCalled = true;

                        s_timeout = (int)s_config.Timeout.TotalMinutes;

                        var section = ConfigurationManager.GetSection("system.web/httpRuntime") as HttpRuntimeSection;
                        if (section == null)
                        {
                            throw new ConfigurationErrorsException(string.Format(SR.Error_Occured_Reading_Config_Secion, "system.web/httpRuntime"));
                        }

                        s_configExecutionTimeout = section.ExecutionTimeout;

                        s_configRegenerateExpiredSessionId = s_config.RegenerateExpiredSessionId;
                        s_configCookieless = s_config.Cookieless;
                        s_configMode = s_config.Mode;

                        if (!s_pollIntervalRegLookedUp)
                            LookUpRegForPollInterval();

                        // The last thing to set in this if-block.
                        s_oneTimeInit = true;
                    }
                }
            }

            if (!initModuleCalled)
            {
                InitModuleFromConfig(app, s_config);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_store != null)
            {
                _store.Dispose();
            }
        }

        /// <inheritdoc />
        public void ReleaseSessionState(HttpContext context)
        {
            // Release session state before executing child request
            TaskAsyncHelper.RunAsyncMethodSynchronously(() => ReleaseSessionStateAsync(context));
        }

        /// <inheritdoc />
        public Task ReleaseSessionStateAsync(HttpContext context)
        {
            if (_acquireCalled && !_releaseCalled && _rqSessionState != null)
            {
                return ReleaseStateAsync(context.ApplicationInstance);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Session start event handler
        /// </summary>
        public event EventHandler Start
        {
            add { _sessionStartEventHandler += value; }
            remove { _sessionStartEventHandler -= value; }
        }

        /// <summary>
        /// Session end event handler
        /// </summary>
        public event EventHandler End
        {
            add
            {
                lock (_onEndTarget)
                {
                    if (_store != null && _onEndTarget.SessionEndEventHandlerCount == 0)
                    {
                        _supportSessionExpiry = _store.SetItemExpireCallback(
                            _onEndTarget.RaiseSessionOnEnd);
                    }
                    ++_onEndTarget.SessionEndEventHandlerCount;
                }
            }
            remove
            {
                lock (_onEndTarget)
                {
                    --_onEndTarget.SessionEndEventHandlerCount;
                    if (_store != null && _onEndTarget.SessionEndEventHandlerCount == 0)
                    {
                        _store.SetItemExpireCallback(null);
                        _supportSessionExpiry = false;
                    }
                }
            }
        }

        private SessionStateStoreProviderAsyncBase SecureInstantiateAsyncProvider(ProviderSettings settings)
        {
            return
                (SessionStateStoreProviderAsyncBase)
                    ProvidersHelper.InstantiateProvider(settings, typeof(SessionStateStoreProviderAsyncBase));
        }

        // Create an instance of the custom store as specified in the config file
        private SessionStateStoreProviderAsyncBase InitCustomStore(SessionStateSection config)
        {
            string providerName = config.CustomProvider;
            if (string.IsNullOrEmpty(providerName))
            {
                throw new ConfigurationErrorsException(
                    string.Format(SR.Invalid_session_custom_provider, providerName),
                    config.ElementInformation.Properties["customProvider"].Source,
                    config.ElementInformation.Properties["customProvider"].LineNumber);
            }

            ProviderSettings ps = config.Providers[providerName];
            if (ps == null)
            {
                throw new ConfigurationErrorsException(
                    string.Format(SR.Missing_session_custom_provider, providerName),
                    config.ElementInformation.Properties["customProvider"].Source,
                    config.ElementInformation.Properties["customProvider"].LineNumber);
            }

            return SecureInstantiateAsyncProvider(ps);
        }

        private void InitModuleFromConfig(HttpApplication app, SessionStateSection config)
        {
            if (config.Mode == SessionStateMode.Off)
            {
                return;
            }

            app.AddOnAcquireRequestStateAsync(BeginAcquireState, EndAcquireState);
            app.AddOnReleaseRequestStateAsync(BeginOnReleaseState, EndOnReleaseState);
            app.AddOnEndRequestAsync(BeginOnEndRequest, EndOnEndRequest);

            if (config.Mode == SessionStateMode.Custom)
            {
                _store = InitCustomStore(config);
            }
            else if(config.Mode == SessionStateMode.InProc)
            {
                _store = new InProcSessionStateStoreAsync();
                _store.Initialize(null, null);
            }
            else
            {
                throw new ConfigurationErrorsException(SR.Not_Support_SessionState_Mode);
            }

            _idManager = InitSessionIDManager(config);
        }

        private IAsyncResult BeginAcquireState(object source, EventArgs e, AsyncCallback cb, object extraData)
        {
            return TaskAsyncHelper.BeginTask((HttpApplication)source, app => AcquireStateAsync(app), cb, extraData);
        }

        private void EndAcquireState(IAsyncResult result)
        {
            TaskAsyncHelper.EndTask(result);
        }

        private IAsyncResult BeginOnEndRequest(object source, EventArgs e, AsyncCallback cb, object extraData)
        {
            return TaskAsyncHelper.BeginTask((HttpApplication)source, app => OnEndRequestAsync(app), cb, extraData);
        }

        private void EndOnEndRequest(IAsyncResult result)
        {
            TaskAsyncHelper.EndTask(result);
        }

        private IAsyncResult BeginOnReleaseState(object source, EventArgs e, AsyncCallback cb, object extraData)
        {
            return TaskAsyncHelper.BeginTask((HttpApplication)source, app => ReleaseStateAsync(app), cb, extraData);
        }

        private void EndOnReleaseState(IAsyncResult result)
        {
            TaskAsyncHelper.EndTask(result);
        }

        private ISessionIDManager InitSessionIDManager(SessionStateSection config)
        {
            string sessionIdManagerType = config.SessionIDManagerType;
            ISessionIDManager iManager;

            if (string.IsNullOrEmpty(sessionIdManagerType))
            {
                iManager = new SessionIDManager();
            }
            else
            {
                Type managerType = Type.GetType(sessionIdManagerType, true /*throwOnError*/, false /*ignoreCase*/);
                CheckAssignableType(typeof(ISessionIDManager), managerType, config, "sessionIDManagerType");

                iManager = (ISessionIDManager)Activator.CreateInstance(managerType);
            }

            iManager.Initialize();

            return iManager;
        }

        private static void CheckAssignableType(Type baseType, Type type, ConfigurationElement configElement,
            string propertyName)
        {
            if (baseType == null) throw new ArgumentNullException("baseType");

            if (!baseType.IsAssignableFrom(type))
            {
                throw new ConfigurationErrorsException(
                    string.Format(SR.Type_doesnt_inherit_from_type, type.FullName, baseType.FullName),
                    configElement.ElementInformation.Properties[propertyName].Source,
                    configElement.ElementInformation.Properties[propertyName].LineNumber);
            }
        }

        private void ResetPerRequestFields()
        {
            _rqSessionState = null;
            _rqId = null;
            _rqSessionItems = null;
            _rqStaticObjects = null;
            _rqIsNewSession = false;
            _rqSessionStateNotFound = true;
            _rqRequiresState = false;
            _rqReadonly = false;
            _rqItem = null;
            _rqContext = null;
            _rqLockId = null;
            _rqExecutionTimeout = TimeSpan.Zero;
            _rqAddedCookie = false;
            _rqIdNew = false;
            _rqActionFlags = 0;
            _rqSupportSessionIdReissue = false;
        }

        private void RaiseOnStart(EventArgs e)
        {
            if (_sessionStartEventHandler != null)
                _sessionStartEventHandler(this, e);
        }

        private void OnStart(EventArgs e)
        {
            RaiseOnStart(e);
        }

        private async Task AcquireStateAsync(HttpApplication app)
        {
            HttpContext context = app.Context;
            _acquireCalled = true;
            _releaseCalled = false;
            ResetPerRequestFields();

            _rqContext = new HttpContextWrapper(context);

            SessionEventSource.Log.SessionDataBegin(context);

            /* Notify the store we are beginning to get process request */
            _store.InitializeRequest(_rqContext);

            /* determine if the request requires state at all */
            _rqRequiresState = SessionStateUtility.IsSessionStateRequired(context);

            // SessionIDManager may need to do a redirect if cookieless setting is AutoDetect
            if (_idManager.InitializeRequest(context, false, out _rqSupportSessionIdReissue))
            {

                SessionEventSource.Log.SessionDataEnd(context);
                return;
            }

            /* Get sessionid */
            _rqId = _idManager.GetSessionID(context);
            if (!_rqRequiresState)
            {
                if (_rqId != null && !_store.SkipKeepAliveWhenUnused)
                {
                    // Still need to update the sliding timeout to keep session alive.
                    // There is a plan to skip this for perf reason.  But it was postponed to
                    // after Whidbey.
                    await _store.ResetItemTimeoutAsync(_rqContext, _rqId, GetCancellationToken());
                }

                SessionEventSource.Log.SessionDataEnd(context);
                return;
            }

            // If the page is marked as DEBUG, HttpContext.Timeout will return a very large value (~1 year)
            // In this case, we want to use the executionTimeout value specified in the config to avoid
            // PollLockedSession to run forever.
            _rqExecutionTimeout = s_configExecutionTimeout;

            /* determine if we need just read-only access */
            _rqReadonly = SessionStateUtility.IsSessionStateReadOnly(context);

            if (_rqId != null)
            {
                /* get the session state corresponding to this session id */
                await GetSessionStateItemAsync();
            }
            else
            {
                /* if there's no id yet, create it */
                bool redirected = CreateSessionId();

                _rqIdNew = true;

                if (redirected)
                {
                    if (s_configRegenerateExpiredSessionId)
                    {
                        // See inline comments in CreateUninitializedSessionState()
                        await _store.CreateUninitializedItemAsync(_rqContext, _rqId, s_timeout, GetCancellationToken());
                    }

                    SessionEventSource.Log.SessionDataEnd(context);
                    return;
                }
            }

            await CompleteAcquireStateAsync(context);
        }

        private bool SessionIdManagerUseCookieless
        {
            get
            {
                // For container created by custom session state module,
                // sorry, we currently don't have a way to tell and thus we rely blindly
                // on cookieMode.
                return ConfigCookieless == HttpCookieMode.UseUri;
            }
        }

        private bool CreateSessionId()
        {
            // CreateSessionId should be called only if:
            Debug.Assert(_rqId == null || // Session id isn't found in the request, OR
                         (_rqSessionStateNotFound && // The session state isn't found, AND
                          s_configRegenerateExpiredSessionId && // We are regenerating expired session id, AND
                          _rqSupportSessionIdReissue && // This request supports session id re-issue, AND
                          !_rqIdNew), // The above three condition should imply the session id
                                      // isn't just created, but is sent by the request.
                "CreateSessionId should be called only if we're generating new id, or re-generating expired one");

            bool redirected;
            _rqId = _idManager.CreateSessionID(_rqContext.ApplicationInstance.Context);
            _idManager.SaveSessionID(_rqContext.ApplicationInstance.Context, _rqId, out redirected, out _rqAddedCookie);

            return redirected;
        }

        private void RegisterEnsureStateStoreItemLocked()
        {
            // Item is locked yet here only if this is a new session
            if (!_rqSessionStateNotFound)
            {
                return;
            }

            _rqContext.Response.AddOnSendingHeaders(
                _ => TaskAsyncHelper.RunAsyncMethodSynchronously(EnsureStateStoreItemLockedAsync));
        }

        private async Task EnsureStateStoreItemLockedAsync()
        {
            if (!_acquireCalled || _releaseCalled)
                return;

            // Ensure ownership of the session state item here as the session ID now can be put on the wire (by Response.Flush)
            // and the client can initiate a request before this one reaches OnReleaseState and thus causing a race condition.
            // Note: It changes when we call into the Session Store provider. Now it may happen at BeginAcquireState instead of OnReleaseState.

            // Item is locked yet here only if this is a new session
            if (!_rqSessionStateNotFound)
            {
                return;
            }

            Debug.Assert(_rqId != null, "Session State ID must exist");
            Debug.Assert(_rqItem != null, "Session State item must exist");

            if (_rqId == null)
                throw new InvalidOperationException("_rqId == null");

            // Store the item if already have been created
            await _store.SetAndReleaseItemExclusiveAsync(_rqContext, _rqId, _rqItem, _rqLockId, true/*_rqSessionStateNotFound*/, GetCancellationToken());

            // Lock Session State Item in Session State Store
            await LockSessionStateItemAsync();


            // Mark as old session here. The SessionState is fully initialized, the item is locked
            _rqSessionStateNotFound = false;
        }

        // Called when AcquireState is done.  This function will add the returned
        // SessionStateStore item to the request context.
        private async Task CompleteAcquireStateAsync(HttpContext context)
        {
            try
            {
                if (_rqItem != null)
                {
                    _rqSessionStateNotFound = false;

                    if ((_rqActionFlags & SessionStateActions.InitializeItem) != 0)
                    {
                        _rqIsNewSession = true;
                    }
                    else
                    {
                        _rqIsNewSession = false;
                    }
                }
                else
                {
                    _rqIsNewSession = true;
                    _rqSessionStateNotFound = true;

                    // We couldn't find the session state.
                    if (!_rqIdNew && // If the request has a session id, that means the session state has expired
                        s_configRegenerateExpiredSessionId && // And we're asked to regenerate expired session
                        _rqSupportSessionIdReissue)
                    {
                        // And this request support session id reissue
                        // We will generate a new session id for this expired session state
                        bool redirected = CreateSessionId();

                        if (redirected)
                        {
                            await _store.CreateUninitializedItemAsync(_rqContext, _rqId, s_timeout, GetCancellationToken());
                        }
                    }
                }

                InitStateStoreItem(true);

                if (_rqIsNewSession)
                {
                    OnStart(EventArgs.Empty);
                }

                // lock free session doesn't need this
                if(!AppSettings.AllowConcurrentRequestsPerSession)
                {
                    RegisterEnsureStateStoreItemLocked();
                }
            }
            finally
            {
                SessionEventSource.Log.SessionDataEnd(context);
            }
        }

        private void InitStateStoreItem(bool addToContext)
        {
            Debug.Assert(_rqId != null, "_rqId != null");

            if (_rqItem == null)
            {
                _rqItem = _store.CreateNewStoreData(_rqContext, s_timeout);
            }

            _rqSessionItems = _rqItem.Items;
            if (_rqSessionItems == null)
            {
                throw new HttpException(string.Format(SR.Null_value_for_SessionStateItemCollection));
            }

            // No check for null because we allow our custom provider to return a null StaticObjects.
            _rqStaticObjects = _rqItem.StaticObjects;

            _rqSessionItems.Dirty = false;

            _rqSessionState = new HttpSessionStateContainer(
                _rqId,
                _rqSessionItems,
                _rqStaticObjects,
                _rqItem.Timeout,
                _rqIsNewSession,
                ConfigCookieless,
                s_configMode,
                _rqReadonly);

            if (addToContext)
            {
                SessionStateUtility.AddHttpSessionStateToContext(_rqContext.ApplicationInstance.Context, _rqSessionState);
            }
        }

        private async Task LockSessionStateItemAsync()
        {
            Debug.Assert(_rqId != null, "_rqId != null");

            if (!_rqReadonly)
            {
                GetItemResult result = await _store.GetItemExclusiveAsync(_rqContext, _rqId, GetCancellationToken());
                SessionStateStoreData storedItem;
                bool locked;
                TimeSpan lockAge;

                Debug.Assert(result != null, "Must succeed in retrieving item from the store through _rqId");
                ExtractValuesFromGetItemResult(result, out storedItem, out locked, out lockAge, out _rqLockId,
                    out _rqActionFlags);
                Debug.Assert(storedItem != null, "Must succeed in locking session state item.");
            }
        }

        private async Task GetSessionStateItemAsync()
        {
            bool isCompleted = false;
            bool isQueued = false;

            try
            {
                while (!isCompleted)
                {
                    isCompleted = true;
                    bool locked = false;
                    TimeSpan lockAge = default(TimeSpan);

                    Debug.Assert(_rqId != null, "_rqId != null");

                    if (_rqReadonly || AppSettings.AllowConcurrentRequestsPerSession)
                    {
                        GetItemResult result = await _store.GetItemAsync(_rqContext, _rqId, GetCancellationToken());
                        if (result != null)
                        {
                            ExtractValuesFromGetItemResult(result, out _rqItem, out locked, out lockAge, out _rqLockId,
                                out _rqActionFlags);
                        }
                    }
                    else
                    {
                        GetItemResult result = await _store.GetItemExclusiveAsync(_rqContext, _rqId, GetCancellationToken());
                        if (result != null)
                        {
                            ExtractValuesFromGetItemResult(result, out _rqItem, out locked, out lockAge, out _rqLockId,
                            out _rqActionFlags);
                        }

                        // WebForm and WebService Session Access Concurrency Issue
                        // If we have an expired session, we need to insert the state in the store here to
                        // ensure serialized access in case more than one entity requests it simultaneously.
                        // If the state has already been created before, CreateUninitializedSessionState is a no-op.
                        if (_rqItem == null && locked == false && _rqId != null)
                        {
                            if (!(ConfigCookieless == HttpCookieMode.UseUri && s_configRegenerateExpiredSessionId))
                            {
                                await _store.CreateUninitializedItemAsync(_rqContext, _rqId, s_timeout, GetCancellationToken());

                                GetItemResult getItemResult =
                                    await _store.GetItemExclusiveAsync(_rqContext, _rqId, GetCancellationToken());

                                if (getItemResult != null)
                                {
                                    ExtractValuesFromGetItemResult(getItemResult, out _rqItem, out locked, out lockAge,
                                        out _rqLockId, out _rqActionFlags);
                                }
                            }
                        }
                    }

                    // We didn't get it because it's locked....
                    if (_rqItem == null && locked)
                    {
                        if (!isQueued)
                        {
                            QueueRef();
                            isQueued = true;
                        }
                        
                        if (lockAge >= _rqExecutionTimeout)
                        {
                            /* Release the lock on the item, which is held by another thread*/
                            await _store.ReleaseItemExclusiveAsync(_rqContext, _rqId, _rqLockId, GetCancellationToken());
                        }

                        isCompleted = false;
                    }

                    if (!isCompleted)
                    {
                        await Task.Delay(s_pollingTimespan);
                    }
                }
            }
            finally
            {
                if (isQueued)
                {
                    DequeRef();
                }
            }
        }

        private void QueueRef()
        {
            //
            // Check the limit
            int count = 0;
            s_queuedRequestsNumPerSession.TryGetValue(_rqId, out count);

            if (count >= AppSettings.RequestQueueLimitPerSession)
            {
                throw new HttpException(SR.Request_Queue_Limit_Per_Session_Exceeded);
            }

            //
            // Add ref
            s_queuedRequestsNumPerSession.AddOrUpdate(_rqId, 1, (key, value) => value + 1);
        }

        private void DequeRef()
        {
            // Decrement the counter
            if (s_queuedRequestsNumPerSession.AddOrUpdate(_rqId, 0, (key, value) => value - 1) == 0)
            {
                //
                // Remove the element when no more references
                ((ICollection<KeyValuePair<string, int>>)s_queuedRequestsNumPerSession).Remove(new KeyValuePair<string, int>(_rqId, 0));
            }
        }

        private void ExtractValuesFromGetItemResult(GetItemResult result, out SessionStateStoreData rqItem,
            out bool locked, out TimeSpan lockAge, out object rqLockId, out SessionStateActions rqActionFlags)
        {
            rqItem = result.Item;
            locked = result.Locked;
            lockAge = result.LockAge;
            rqLockId = result.LockId;
            rqActionFlags = result.Actions;
        }
        
        private static void LookUpRegForPollInterval()
        {
            lock (PollIntervalRegLock)
            {
                if (s_pollIntervalRegLookedUp)
                    return;
                try
                {
                    object o = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\ASP.NET",
                        "SessionStateLockedItemPollInterval", 0);
                    if (o != null && (o is int || o is uint) && (int)o > 0)
                        s_lockedItemPollingInterval = (int)o;

                    s_pollingTimespan = TimeSpan.FromMilliseconds(s_lockedItemPollingInterval);
                    s_pollIntervalRegLookedUp = true;
                }
                catch
                {
                    // ignore exceptions
                }
            }
        }
        
        // Release session state
        private Task ReleaseStateAsync(HttpApplication application)
        {
            _releaseCalled = true;

            if (_rqSessionState == null)
            {
                return Task.CompletedTask;
            }

            SessionStateUtility.RemoveHttpSessionStateFromContext(_rqContext.ApplicationInstance.Context);

            /*
             * Don't store untouched new sessions.
             */

            if (
                // The store doesn't have the session state.
                // ( Please note we aren't checking _rqIsNewSession because _rqIsNewSession
                // is lalso true if the item is converted from temp to perm in a GetItemXXX() call.)
                _rqSessionStateNotFound

                // OnStart is not defined
                && _sessionStartEventHandler == null

                // Nothing has been stored in session state
                && !_rqSessionItems.Dirty
                && (_rqStaticObjects == null || _rqStaticObjects.NeverAccessed)
                )
            {
                RemoveSessionId(application.Context);
                return Task.CompletedTask;
            }

            return ReleaseStateAsyncImpl(application);
        }

        private async Task ReleaseStateAsyncImpl(HttpApplication application)
        {
            bool setItemCalled = false;

            HttpApplication app = application;
            HttpContext context = app.Context;

            if (_rqSessionState.IsAbandoned)
            {
                if (_rqSessionStateNotFound)
                {
                    // The store provider doesn't have it, and so we don't need to remove it from the store.

                    // However, if the store provider supports session expiry, and we have a Session_End in global.asax,
                    // we need to explicitly call Session_End.
                    if (_supportSessionExpiry)
                    {
                        _onEndTarget.RaiseSessionOnEnd(_rqId, _rqItem);
                    }
                }
                else
                {
                    Debug.Assert(_rqItem != null, "_rqItem cannot null if it's not a new session");

                    // Remove it from the store because the session is abandoned.
                    await _store.RemoveItemAsync(_rqContext, _rqId, _rqLockId, _rqItem, GetCancellationToken());
                }
            }
            else if (!_rqReadonly ||
                     (_rqReadonly &&
                      _rqIsNewSession &&
                      _sessionStartEventHandler != null &&
                      !SessionIdManagerUseCookieless))
            {
                // We save it only if there is no error, and if something has changed (unless it's a new session)
                if (context.Error == null // no error
                    && (_rqSessionStateNotFound
                        || _rqSessionItems.Dirty // SessionItems has changed.
                        || (_rqStaticObjects != null && !_rqStaticObjects.NeverAccessed)
                        // Static objects have been accessed
                        || _rqItem.Timeout != _rqSessionState.Timeout // Timeout value has changed
                        )
                    )
                {
                    if (_rqItem.Timeout != _rqSessionState.Timeout)
                    {
                        _rqItem.Timeout = _rqSessionState.Timeout;
                    }

                    setItemCalled = true;
                    await _store.SetAndReleaseItemExclusiveAsync(_rqContext, _rqId, _rqItem,
                            _rqLockId, _rqSessionStateNotFound, GetCancellationToken());
                }
                else
                {
                    // Can't save it because of various reason.  Just release our exclusive lock on it.
                    if (!_rqSessionStateNotFound)
                    {
                        Debug.Assert(_rqItem != null, "_rqItem cannot null if it's not a new session");
                        await _store.ReleaseItemExclusiveAsync(_rqContext, _rqId, _rqLockId,
                                GetCancellationToken());
                    }
                }
            }

            if (!setItemCalled)
            {
                RemoveSessionId(context);
            }
        }

        private void RemoveSessionId(HttpContext context)
        {
            if (_rqAddedCookie && !context.Response.HeadersWritten)
            {
                _idManager.RemoveSessionID(_rqContext.ApplicationInstance.Context);
            }
        }

        /*
         * End of request processing. Possibly does release if skipped due to errors
         */

        /// <devdoc>
        ///     <para>[To be supplied.]</para>
        /// </devdoc>
        private async Task OnEndRequestAsync(HttpApplication application)
        {
            HttpApplication app = application;
            HttpContext context = app.Context;

            /* determine if the request requires state at all */
            if (!_rqRequiresState)
            {
                return;
            }

            try
            {
                if (!_releaseCalled)
                {
                    if (_acquireCalled)
                    {
                        /*
                         * need to do release here if the request short-circuited due to an error
                         */
                        await ReleaseStateAsync(app);
                    }
                    else
                    {
                        /*
                         * 'advise' -- update session timeout
                         */

                        if (_rqContext == null)
                        {
                            _rqContext = new HttpContextWrapper(context);
                        }

                        // We haven't called BeginAcquireState.  So we have to call these InitializeRequest
                        // methods here.
                        bool dummy;
                        _store.InitializeRequest(_rqContext);
                        _idManager.InitializeRequest(context, true, out dummy);

                        string id = _idManager.GetSessionID(context);
                        if (id != null)
                        {
                            await _store.ResetItemTimeoutAsync(_rqContext, id, GetCancellationToken());
                        }
                    }
                }

                /* Notify the store we are finishing a request */
                await _store.EndRequestAsync(_rqContext);
            }
            finally
            {
                _acquireCalled = false;
                _releaseCalled = false;
                ResetPerRequestFields();
            }
        }

        private static CancellationToken GetCancellationToken()
        {
            return CancellationToken.None;
        }
    }
}
