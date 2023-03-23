// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See the License.txt file in the project root for full license information.

namespace Microsoft.AspNet.SessionState
{
    using Microsoft.AspNet.SessionState.Resources;
    using Microsoft.Azure.Cosmos;
    using Microsoft.Azure.Cosmos.Scripts;
    using System;
    using System.Collections.Specialized;
    using System.Configuration;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.IO.Compression;
    using System.Text.Json.Serialization;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web;
    using System.Web.Configuration;
    using System.Web.SessionState;

    /// <summary>
    /// Async version of CosmosDB SessionState Provider
    /// </summary>
    public class CosmosDBSessionStateProviderAsync : SessionStateStoreProviderAsyncBase
    {
        private static readonly int DefaultLockCookie = 1;

        private static string s_endPoint;
        private static string s_authKey;
        private static bool s_oneTimeInited;
        private static object s_lock = new object();
        private static string s_dbId;
        private static string s_containerId;
        private static int s_offerThroughput;
        private static bool s_compressionEnabled;
        private static int s_timeout;
        private static CosmosClient s_client;
        private static IndexingPolicy s_indexNone = new IndexingPolicy()
        {
            IndexingMode = IndexingMode.Consistent,
            ExcludedPaths =
            {
                new ExcludedPath() { Path = "/*" }
            }
        };

        #region CosmosDB Stored Procedures            
        private const string CreateSessionStateItemSPID = "CreateSessionStateItem2";
        private const string GetStateItemSPID = "GetStateItem2";
        private const string GetStateItemExclusiveSPID = "GetStateItemExclusive";
        private const string ReleaseItemExclusiveSPID = "ReleaseItemExclusive";
        private const string RemoveStateItemSPID = "RemoveStateItem2";
        private const string ResetItemTimeoutSPID = "ResetItemTimeout";
        private const string UpdateSessionStateItemSPID = "UpdateSessionStateItem";

        private const string CreateSessionStateItemSP = @"
            function CreateSessionStateItem2(sessionId, timeout, lockCookie, sessionItem, uninitialized) {
                var collection = getContext().getCollection();
                var collectionLink = collection.getSelfLink();
                var response = getContext().getResponse();

                if (!sessionId) {
                    throw new Error('sessionId cannot be null');
                }
                if (!timeout) {
                    throw new Error('timeout cannot be null');
                }
                if (!lockCookie) {
                    throw new Error('lockCookie cannot be null');
                }

                var sessionStateItem = { id: sessionId, lockDate: (new Date()).getTime(), lockAge: 0, lockCookie:lockCookie, 
                    ttl: timeout, locked: false, sessionItem: sessionItem, uninitialized: uninitialized };
                collection.createDocument(collectionLink, sessionStateItem,
                    function (err, documentCreated) {
                            if (err)
                            {
                                // When creating an uninitialized item, we are doing so just to make sure it gets created.
                                // If another request has done the same, it doesn't matter who won the race. Read/Write
                                // locked/exclusive access is determined later via a separate GetStateItem call.
                                if (uninitialized && err.number == 409) // message: 'Resource with specified id or name already exists.'
                                {
                                    response.setBody({message: 'Document already exists. No need to recreate uninitialized document.', error: err});
                                }

                                throw err;
                            }
                            else
                            {
                                response.setBody({documentCreated: documentCreated});
                            }
                });
            }";


        private const string GetStateItemSP = @"
            function GetStateItem2(sessionId) {
                var collection = getContext().getCollection();
                var collectionLink = collection.getSelfLink();
                var response = getContext().getResponse();

                if (!sessionId) {
                    throw new Error('sessionId cannot be null');
                }

                tryGetStateItem();

                function tryGetStateItem(continuation) {
                    var requestOptions = { continuation: continuation};
                    var query = 'select * from root r where r.id = ""' + sessionId + '""';
                    var isAccepted = collection.queryDocuments(collectionLink, query, requestOptions,
                        function(err, documents, responseOptions) {
                            if (err)
                            {
                                throw err;
                            }
                            if (documents.length > 0)
                            {
                                var doc = documents[0];
                                if (doc.locked)
                                {
                                    doc.lockAge = Math.round(((new Date()).getTime() - doc.lockDate) / 1000);
                                    var isAccepted = collection.replaceDocument(doc._self, doc,
                                        function(err, updatedDocument, responseOptions) {
                                        if (err)
                                        {
                                            throw err;
                                        }
                                        var responseDoc = { id: doc.id, lockAge: 0, lockCookie: updatedDocument.lockCookie, ttl: null, 
                                                        locked: true, sessionItem: null, uninitialized: null };
                                        response.setBody(responseDoc);
                                    });
                                    if (!isAccepted)
                                    {
                                        throw new Error('The SP timed out.');
                                    }
                                }
                                else
                                {
                                    // using write operation to reset the TTL
                                    var isAccepted = collection.replaceDocument(doc._self, doc,
                                        function(err, updatedDocument, responseOptions) {
                                        if (err)
                                        {
                                            throw err;
                                        }
                                        response.setBody(updatedDocument);
                                    });
                                    if (!isAccepted)
                                    {
                                        throw new Error('The SP timed out.');
                                    }
                                }
                            }
                            else if (responseOptions.continuation)
                            {
                                tryGetStateItem(responseOptions.continuation);
                            }
                            else
                            {
                                var responseDoc = { id: '', lockAge: null, lockCookie: null, ttl: null, 
                                                    locked: false, sessionItem: null, uninitialized: null };
                                response.setBody(responseDoc);
                            }
                        });

                    if (!isAccepted) {
                        throw new Error('The SP timed out.');
            }
                }
            }";

        private const string GetStateItemExclusiveSP = @"
            function GetStateItemExclusive(sessionId) {
                var collection = getContext().getCollection();
                var collectionLink = collection.getSelfLink();
                var response = getContext().getResponse();

                if (!sessionId) {
                    throw new Error('sessionId cannot be null');
                }

                tryGetStateItemExclusive();

                function tryGetStateItemExclusive(continuation) {
                    var requestOptions = { continuation: continuation};
                    var query = 'select * from root r where r.id = ""' + sessionId + '""';
                    var isAccepted = collection.queryDocuments(collectionLink, query, requestOptions,
                        function(err, documents, responseOptions) {
                            if (err)
                            {
                                throw err;
                            }
                            if (documents.length > 0)
                            {
                                var doc = documents[0];
                                if (doc.locked)
                                {
                                    doc.lockAge = Math.round(((new Date()).getTime() - doc.lockDate) / 1000);
                                    var isAccepted = collection.replaceDocument(doc._self, doc,
                                        function(err, updatedDocument, responseOptions) {
                                        if (err)
                                        {
                                            throw err;
                                        }
                                    var responseDoc = { id: doc.id, lockAge: updatedDocument.lockAge, lockCookie: updatedDocument.lockCookie, ttl: null, 
                                                        locked: true, sessionItem: null, uninitialized: null };
                                        response.setBody(responseDoc);
                                    });
                                    if (!isAccepted)
                                    {
                                        throw new Error('The SP timed out.');
                                    }
                                }
                                else
                                {
                                    var responseDoc = { id: doc.id, lockAge: doc.lockAge, lockCookie: doc.lockCookie + 1, ttl: doc.ttl,
                                                        locked: false, sessionItem: doc.sessionItem, uninitialized: doc.uninitialized };
                                    doc.lockAge = 0;
                                    doc.lockCookie += 1;
                                    doc.locked = true;
                                    // CosmosDB sprocs are 'atomic' so no need to worry about a race in between the initial query and this update.
                                    var isAccepted = collection.replaceDocument(doc._self, doc,
                                        function(err, updatedDocument, responseOptions) {
                                        if (err)
                                        {
                                            throw err;
                                        }
                                        response.setBody(responseDoc);
                                    });
                                    if (!isAccepted)
                                    {
                                        throw new Error('The SP timed out.');
                                    }
                                }
                            }
                            else if (responseOptions.continuation)
                            {
                                tryGetStateItemExclusive(responseOptions.continuation);
                            }
                            else
                            {
                                var responseDoc = { id: '', lockAge: null, lockCookie: null, ttl: null, 
                                                    locked: false, sessionItem: null, uninitialized: null };
                                response.setBody(responseDoc);
                            }
                        });

                    if (!isAccepted) {
                        throw new Error('The SP timed out.');
                    }
                }
            }";

        private const string ReleaseItemExclusiveSP = @"
            function ReleaseItemExclusive(sessionId, lockCookie) {
                var collection = getContext().getCollection();
                var collectionLink = collection.getSelfLink();
                var response = getContext().getResponse();

                if (!sessionId) {
                    throw new Error('sessionId cannot be null');
                }
                TryReleaseItemExclusive();

                function TryReleaseItemExclusive(continuation) {
                    var requestOptions = { continuation: continuation};
                    var query = 'select * from root r where r.id = ""' + sessionId + '"" and r.lockCookie = ' + lockCookie;
                    var isAccepted = collection.queryDocuments(collectionLink, query, requestOptions,
                        function(err, documents, responseOptions) {
                            if (err)
                            {
                                throw err;
                            }
                            if (documents.length > 0)
                            {
                                var doc = documents[0];
                                doc.locked = false
                                if(doc.uninitialized)
                                {
                                    doc.uninitialized = false;
                                }
                                var isAccepted = collection.replaceDocument(doc._self, doc,
                                    function(err, updatedDocument, responseOptions) {
                                    if (err)
                                    {
                                        throw err;
                                    }
                                    response.setBody({ updated: true});
                                });
                                if (!isAccepted)
                                {
                                    throw new Error('The SP timed out.');
                                }
                            }
                            else if (responseOptions.continuation)
                            {
                                TryReleaseItemExclusive(responseOptions.continuation);
                            }
                            else
                            {
                                response.setBody({ updated: false });
                            }
                    });

                    if (!isAccepted) {
                        throw new Error('The SP timed out.');
                    }
                }
            }";

        private const string RemoveStateItemSP = @"
            function RemoveStateItem2(sessionId, lockCookie) {
                var collection = getContext().getCollection();
                var collectionLink = collection.getSelfLink();
                var response = getContext().getResponse();

                if (!sessionId) {
                    throw new Error('sessionId cannot be null');
                }
                if (!lockCookie)
                {
                    throw new Error('lockCookie cannot be null');
                }
                TryRemoveStateItem();

                function TryRemoveStateItem(continuation) {
                    var requestOptions = { continuation: continuation};
                    var query = 'select * from root r where r.id = ""' + sessionId + '"" and r.lockCookie = ' + lockCookie;
                    var isAccepted = collection.queryDocuments(collectionLink, query, requestOptions,
                        function(err, documents, responseOptions) {
                    if (err)
                    {
                        throw err;
                    }
                    if (documents.length > 0)
                    {
                        var doc = documents[0];
                        var isAccepted = collection.deleteDocument(doc._self,
                            function(err, updatedDocument, responseOptions) {
                                if (err)
                                {
                                    throw err;
                                }
                                response.setBody({ updated: true });
                            });
                        if (!isAccepted)
                        {
                            throw new Error('The SP timed out.');
                        }
                    }
                    else if (responseOptions.continuation)
                    {
                        TryRemoveStateItem(responseOptions.continuation);
                    }
                    else
                    {
                        response.setBody({ updated: false });
                    }
                });

                    if (!isAccepted) {
                        throw new Error('The SP timed out.');
                    }
                }
            }";

        private const string ResetItemTimeoutSP = @"
            function ResetItemTimeout(sessionId) {
                var collection = getContext().getCollection();
                var collectionLink = collection.getSelfLink();
                var response = getContext().getResponse();

                if (!sessionId) {
                    throw new Error('sessionId cannot be null');
                }

                tryResetItemTimeout();

                function tryResetItemTimeout(continuation) {
                    var requestOptions = { continuation: continuation};
                    var query = 'select * from root r where r.id = ""' + sessionId + '""';
                    var isAccepted = collection.queryDocuments(collectionLink, query, requestOptions,
                        function(err, documents, responseOptions) {
                            if (err)
                            {
                                throw err;
                            }
                            if (documents.length > 0)
                            {
                                var doc = documents[0];
                                // using write operation to reset the TTL
                                var isAccepted = collection.replaceDocument(doc._self, doc,
                                    function(err, updatedDocument, responseOptions) {
                                    if (err)
                                    {
                                        throw err;
                                    }
                                    response.setBody({ updated: true });
                                });
                                if (!isAccepted)
                                {
                                    throw new Error('The SP timed out.');
                                }
                            }
                            else if (responseOptions.continuation)
                            {
                                tryResetItemTimeout(responseOptions.continuation);
                            }
                            else
                            {
                                response.setBody({ updated: false });
                            }
                        });

                    if (!isAccepted) {
                        throw new Error('The SP timed out.');
                    }
                }
            }";

        private const string UpdateSessionStateItemSP = @"
            function UpdateSessionStateItem(sessionId, lockCookie, timeout, sessionItem) {
                var collection = getContext().getCollection();
                var collectionLink = collection.getSelfLink();
                var response = getContext().getResponse();

                if (!sessionId) {
                    throw new Error('');
                }

                if (!lockCookie) {
                    throw new Error('');
                }

                tryUpdateSessionStateItem();

                function tryUpdateSessionStateItem(continuation) {
                    var requestOptions = { continuation: continuation};
                    var query = 'select * from root r where r.id = ""' + sessionId + '"" and r.lockCookie = ' + lockCookie;
                    var isAccepted = collection.queryDocuments(collectionLink, query, requestOptions,
                        function(err, documents, responseOptions) {
                            if (err)
                            {
                                throw err;
                            }
                            if (documents.length > 0)
                            {
                                var doc = documents[0];
                                doc.sessionItem = sessionItem;
                                doc.locked = false;
                                doc.ttl = timeout;
                                var isAccepted = collection.replaceDocument(doc._self, doc,
                                    function(err, updatedDocument, responseOptions) {
                                    if (err)
                                    {
                                        throw err;
                                    }
                                    response.setBody({ updated: true });
                                });
                                if (!isAccepted)
                                {
                                    throw new Error('The SP timed out.');
                                }
                            }
                            else if (responseOptions.continuation)
                            {
                                tryUpdateSessionStateItem(responseOptions.continuation);
                            }
                            else
                            {
                                response.setBody({ updated: false });
                            }
                        });

                    if (!isAccepted) {
                        throw new Error('The SP timed out.');
                    }
                }
            }";
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
                throw new ArgumentNullException("config");
            }
            if (String.IsNullOrEmpty(name))
            {
                name = "CosmosDBSessionStateProviderAsync";
            }

            var ssc = (SessionStateSection)ConfigurationManager.GetSection("system.web/sessionState");
            Initialize(name, config, ssc, WebConfigurationManager.AppSettings);
        }

        internal void Initialize(string name, NameValueCollection providerConfig, SessionStateSection ssc, NameValueCollection appSettings)
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

                        ParseCosmosDbEndPointSettings(providerConfig, appSettings);

                        s_client = CosmosClientFactory(s_endPoint, s_authKey, ParseCosmosDBClientSettings(providerConfig));

                        // setup CosmosDB
                        CreateDatabaseIfNotExistsAsync().Wait();
                        CreateContainerIfNotExistsAsync().Wait();
                        CreateStoredProceduresIfNotExistsAsync().Wait();

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

        internal static void ResetStaticFields()
        {
            s_endPoint = "";
            s_authKey = "";
            s_dbId = "";
            s_containerId = "";
            s_offerThroughput = 0;
            s_compressionEnabled = false;
            s_oneTimeInited = false;
            s_timeout = 0;
            s_client = null;
        }

        internal string AppId
        {
            get; set;
        }

        internal static string EndPoint
        {
            get { return s_endPoint; }
        }

        internal static string AuthKey
        {
            get { return s_authKey; }
        }

        internal static string DbId
        {
            get { return s_dbId; }
        }

        internal static string ContainerId
        {
            get { return s_containerId; }
        }

        internal static int ThroughPut
        {
            get { return s_offerThroughput; }
        }

        internal static CosmosClient Client
        {
            get { return s_client; }
            set { s_client = value; }
        }

        internal static Func<HttpContext, HttpStaticObjectsCollection> GetSessionStaticObjects
        {
            get; set;
        } = SessionStateUtility.GetSessionStaticObjects;

        internal Func<string, string, CosmosClientOptions, CosmosClient> CosmosClientFactory
        {
            get; set;
        } = (endpoint, authKey, options) => new CosmosClient(endpoint, authKey, options);
        #endregion

        /// <inheritdoc />
        public override SessionStateStoreData CreateNewStoreData(HttpContextBase context, int timeout)
        {
            HttpStaticObjectsCollection staticObjects = null;
            if (context != null)
            {
                staticObjects = GetSessionStaticObjects(context.ApplicationInstance.Context);
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

            string encodedBuf;

            var item = new SessionStateStoreData(new SessionStateItemCollection(),
                        GetSessionStaticObjects(context.ApplicationInstance.Context),
                        timeout);

            SerializeStoreData(item, out encodedBuf, s_compressionEnabled);

            var timeoutInSecs = 60 * timeout;
            await CreateSessionStateItemAsync(id, timeoutInSecs, encodedBuf, true);
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
            return DoGet(context, id, false);
        }

        /// <inheritdoc />
        public override Task<GetItemResult> GetItemExclusiveAsync(HttpContextBase context, string id, CancellationToken cancellationToken)
        {
            return DoGet(context, id, true);
        }

        private async Task<GetItemResult> DoGet(HttpContextBase context, string id, bool exclusive)
        {
            var spName = exclusive ? GetStateItemExclusiveSPID : GetStateItemSPID;
            var spResponse = await ExecuteStoredProcedureWithWrapperAsync<SessionStateItem>(spName, id, id);

            CheckSPResponseAndThrowIfNeeded(spResponse);

            var sessionStateItem = spResponse.Resource;
            if (string.IsNullOrEmpty(sessionStateItem.SessionId))
            {
                return null;
            }

            if (sessionStateItem.Locked.Value)
            {
                return new GetItemResult(null, sessionStateItem.Locked.Value, sessionStateItem.LockAge.Value, sessionStateItem.LockCookie.Value, SessionStateActions.None);
            }
            else
            {
                using (var stream = new MemoryStream(sessionStateItem.SessionItem))
                {
                    var data = DeserializeStoreData(context, stream, s_compressionEnabled);
                    var action = sessionStateItem.Actions.HasValue ? sessionStateItem.Actions.Value : SessionStateActions.None;
                    return new GetItemResult(data, sessionStateItem.Locked.Value, sessionStateItem.LockAge.Value, sessionStateItem.LockCookie.Value, action);
                }
            }
        }

        /// <inheritdoc />
        public override void InitializeRequest(HttpContextBase context) { }

        /// <inheritdoc />
        public override async Task ReleaseItemExclusiveAsync(
            HttpContextBase context,
            string id,
            object lockId,
            CancellationToken cancellationToken)
        {
            var spResponse = await ExecuteStoredProcedureWithWrapperAsync<object>(ReleaseItemExclusiveSPID, id, id, (int)lockId);

            CheckSPResponseAndThrowIfNeeded(spResponse);
        }

        /// <inheritdoc />
        public override async Task RemoveItemAsync(
            HttpContextBase context,
            string id,
            object lockId,
            SessionStateStoreData item,
            CancellationToken cancellationToken)
        {
            var spResponse = await ExecuteStoredProcedureWithWrapperAsync<object>(RemoveStateItemSPID, id, id, (int)lockId);

            CheckSPResponseAndThrowIfNeeded(spResponse);
        }

        /// <inheritdoc />
        public override async Task ResetItemTimeoutAsync(HttpContextBase context, string id, CancellationToken cancellationToken)
        {
            var spResponse = await ExecuteStoredProcedureWithWrapperAsync<object>(ResetItemTimeoutSPID, id, id);

            CheckSPResponseAndThrowIfNeeded(spResponse);
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
            string encodedBuf;
            int lockCookie;

            if (item == null)
            {
                throw new ArgumentNullException("item");
            }
            if (id == null)
            {
                throw new ArgumentNullException("id");
            }

            try
            {
                SerializeStoreData(item, out encodedBuf, s_compressionEnabled);
            }
            catch
            {
                if (!newItem)
                {
                    await ReleaseItemExclusiveAsync(context, id, lockId, cancellationToken);
                }
                throw;
            }
            lockCookie = lockId == null ? DefaultLockCookie : (int)lockId;

            if (newItem)
            {
                await CreateSessionStateItemAsync(id, s_timeout, encodedBuf, false);
            }
            else
            {
                var spResponse = await ExecuteStoredProcedureWithWrapperAsync<object>(UpdateSessionStateItemSPID, id,
                    //sessionId, lockCookie, timeout, sessionItem
                    // SessionStateStoreData.Timeout is in minutes, TTL in DocumentDB is in seconds
                    id, lockCookie, 60 * item.Timeout, encodedBuf);

                CheckSPResponseAndThrowIfNeeded(spResponse);
            }
        }

        /// <inheritdoc />
        public override bool SetItemExpireCallback(SessionStateItemExpireCallback expireCallback)
        {
            return false;
        }

        internal static void ParseCosmosDbEndPointSettings(NameValueCollection providerConfig, NameValueCollection appSettings)
        {
            var endPointSettingKey = providerConfig["cosmosDBEndPointSettingKey"];
            if (string.IsNullOrEmpty(endPointSettingKey))
            {
                throw new ConfigurationErrorsException(string.Format(SR.EmptyConfig_WithName, "cosmosDBEndPointSettingKey"));
            }
            s_endPoint = appSettings[endPointSettingKey];
            if (string.IsNullOrEmpty(s_endPoint))
            {
                throw new ConfigurationErrorsException(string.Format(SR.EmptyConfig_WithName, endPointSettingKey));
            }

            var authKeySettingKey = providerConfig["cosmosDBAuthKeySettingKey"];
            if (string.IsNullOrEmpty(authKeySettingKey))
            {
                throw new ConfigurationErrorsException(string.Format(SR.EmptyConfig_WithName, "cosmosDBAuthKeySettingKey"));
            }
            s_authKey = appSettings[authKeySettingKey];
            if (string.IsNullOrEmpty(s_authKey))
            {
                throw new ConfigurationErrorsException(string.Format(SR.EmptyConfig_WithName, authKeySettingKey));
            }

            s_dbId = providerConfig["databaseId"];
            if (string.IsNullOrEmpty(s_dbId))
            {
                throw new ConfigurationErrorsException(string.Format(SR.EmptyConfig_WithName, "databaseId"));
            }

            s_containerId = providerConfig["containerId"];
            if (string.IsNullOrEmpty(s_containerId))
            {
                s_containerId = providerConfig["collectionId"];
                if (string.IsNullOrEmpty(s_containerId))
                {
                    throw new ConfigurationErrorsException(string.Format(SR.EmptyConfig_WithName, "containerId"));
                }
            }

            if (!int.TryParse(providerConfig["offerThroughput"], out s_offerThroughput))
            {
                s_offerThroughput = 5000;
            }
        }

        internal static CosmosClientOptions ParseCosmosDBClientSettings(NameValueCollection config)
        {
            ConnectionMode conMode;
            if (!Enum.TryParse<ConnectionMode>(config["connectionMode"], out conMode))
            {
                conMode = ConnectionMode.Direct;
            }

            int requestTimeout;
            if (!int.TryParse(config["requestTimeout"], out requestTimeout))
            {
                requestTimeout = 5;
            }

            int maxConnectionLimit;
            if (!int.TryParse(config["maxConnectionLimit"], out maxConnectionLimit))
            {
                maxConnectionLimit = 50;
            }

            int maxRetryAttemptsOnThrottledRequests;
            if (!int.TryParse(config["maxRetryAttemptsOnThrottledRequests"], out maxRetryAttemptsOnThrottledRequests))
            {
                maxRetryAttemptsOnThrottledRequests = 9;
            }

            int maxRetryWaitTimeInSeconds;
            if (!int.TryParse(config["maxRetryWaitTimeInSeconds"], out maxRetryWaitTimeInSeconds))
            {
                maxRetryWaitTimeInSeconds = 10;
            }

            var clientOptions = new CosmosClientOptions
            {
                Serializer = new SystemTextJsonSerializer(null),
                ConnectionMode = conMode,
                RequestTimeout = new TimeSpan(0, 0, requestTimeout),
                GatewayModeMaxConnectionLimit = maxConnectionLimit,
                EnableTcpConnectionEndpointRediscovery = false,
                MaxRetryAttemptsOnRateLimitedRequests = maxRetryAttemptsOnThrottledRequests,
                MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.FromSeconds(maxRetryWaitTimeInSeconds)
            };

            if (Enum.TryParse<ConsistencyLevel>(config["consistencyLevel"], out ConsistencyLevel consistencyLevel))
            {
                clientOptions.ConsistencyLevel = consistencyLevel;
            }

            var preferredLocations = config["preferredLocations"];
            if (!string.IsNullOrEmpty(preferredLocations))
            {
                clientOptions.ApplicationPreferredRegions = preferredLocations.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

                if (clientOptions.ApplicationPreferredRegions.Count > 0)
                {
                    clientOptions.EnableTcpConnectionEndpointRediscovery = true;
                }
            }

            return clientOptions;
        }

        private static async Task CreateDatabaseIfNotExistsAsync()
        {
            await s_client.CreateDatabaseIfNotExistsAsync(s_dbId).ConfigureAwait(false);
        }

        private static async Task CreateContainerIfNotExistsAsync()
        {
            var database = s_client.GetDatabase(s_dbId);
            string partitionKeyPath = $"/id";

            var containerProperties = new ContainerProperties
            {
                Id = s_containerId,
                PartitionKeyPath = partitionKeyPath,
                DefaultTimeToLive = s_timeout,
                IndexingPolicy = s_indexNone
            };

            ContainerResponse response = await database.CreateContainerIfNotExistsAsync(containerProperties, s_offerThroughput > 0 ? s_offerThroughput : (int?)null).ConfigureAwait(false);

            if (response?.Resource?.PartitionKeyPath != partitionKeyPath)
            {
                throw new Exception(String.Format(CultureInfo.CurrentCulture, SR.Container_PKey_Does_Not_Match, s_containerId, partitionKeyPath));
            }
        }

        private async Task CreateStoredProceduresIfNotExistsAsync()
        {
            await CreateSPIfNotExistsAsync(CreateSessionStateItemSPID, CreateSessionStateItemSP);
            await CreateSPIfNotExistsAsync(GetStateItemSPID, GetStateItemSP);
            await CreateSPIfNotExistsAsync(GetStateItemExclusiveSPID, GetStateItemExclusiveSP);
            await CreateSPIfNotExistsAsync(ReleaseItemExclusiveSPID, ReleaseItemExclusiveSP);
            await CreateSPIfNotExistsAsync(RemoveStateItemSPID, RemoveStateItemSP);
            await CreateSPIfNotExistsAsync(ResetItemTimeoutSPID, ResetItemTimeoutSP);
            await CreateSPIfNotExistsAsync(UpdateSessionStateItemSPID, UpdateSessionStateItemSP);
        }

        private static async Task CreateSPIfNotExistsAsync(string spId, string spBody)
        {
            var container = s_client.GetContainer(s_dbId, s_containerId);

            try
            {
                await container.Scripts.ReadStoredProcedureAsync(spId).ConfigureAwait(false);
            }
            catch (CosmosException e)
            {
                if (e.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    var properties = new StoredProcedureProperties(spId, spBody);

                    await container.Scripts.CreateStoredProcedureAsync(properties).ConfigureAwait(false);
                }
                else
                {
                    throw;
                }
            }
        }

        internal static async Task<StoredProcedureExecuteResponse<TValue>> ExecuteStoredProcedureWithWrapperAsync<TValue>(
            string spId,
            string sessionId,
            params dynamic[] spParams)
        {
            try
            {
                var container = s_client.GetContainer(s_dbId, s_containerId);

                return await container.Scripts.ExecuteStoredProcedureAsync<TValue>(spId, new PartitionKey(sessionId), spParams).ConfigureAwait(false);
            }
            catch (CosmosException dce)
            {
                //Status 429: The request rate is too large
                if ((int)dce.StatusCode == 429)
                {
                    throw new Exception(SR.Request_To_CosmosDB_Is_Too_Large);
                }
                else
                {
                    throw;
                }
            }
            catch (AggregateException ae)
            {
                var innerException = ae.InnerException as CosmosException;
                if (innerException != null & (int)innerException.StatusCode == 429)
                {
                    throw new Exception(SR.Request_To_CosmosDB_Is_Too_Large);
                }
                else
                {
                    throw;
                }
            }
        }

        internal async Task CreateSessionStateItemAsync(string sessionid, int timeoutInSec, string encodedSsItems, bool uninitialized)
        {
            var spResponse = await ExecuteStoredProcedureWithWrapperAsync<object>(CreateSessionStateItemSPID, sessionid,
                // sessionId, timeout, lockCookie, sessionItem, uninitialized
                sessionid, timeoutInSec, DefaultLockCookie, encodedSsItems, uninitialized);

            CheckSPResponseAndThrowIfNeeded(spResponse);
        }

        private static void CheckSPResponseAndThrowIfNeeded<T>(StoredProcedureExecuteResponse<T> spResponse)
        {
            if (spResponse.StatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new Exception(spResponse.StatusCode.ToString());
            }
        }

        // Internal code copied from SessionStateUtility
        internal static void SerializeStoreData(SessionStateStoreData item, out string encodedBuf, bool compressionEnabled)
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
                encodedBuf = GetEncodedStringFromMemoryStream(s);
            }
        }

        private static string GetEncodedStringFromMemoryStream(MemoryStream s)
        {
            ArraySegment<byte> bytes = new ArraySegment<byte>();

            if (!s.TryGetBuffer(out bytes))
                return null;

            return Convert.ToBase64String(bytes.Array, bytes.Offset, bytes.Count);
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
                    throw new HttpException(String.Format(CultureInfo.CurrentCulture, SR.Invalid_session_state));
                }
            }
            catch (EndOfStreamException)
            {
                throw new HttpException(String.Format(CultureInfo.CurrentCulture, SR.Invalid_session_state));
            }

            return new SessionStateStoreData(sessionItems, staticObjects, timeout);
        }
    }
}
