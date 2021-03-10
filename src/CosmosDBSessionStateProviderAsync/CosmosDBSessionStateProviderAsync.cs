// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See the License.txt file in the project root for full license information.

namespace Microsoft.AspNet.SessionState
{
    using Microsoft.AspNet.SessionState.Resources;
    using Microsoft.Azure.Documents;
    using Microsoft.Azure.Documents.Client;
    using System;
    using System.Collections.Specialized;
    using System.Configuration;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.IO.Compression;
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
        private static string s_partitionKey = "";
        private static int s_partitionNumUsedBySessionProvider;
        private static bool s_oneTimeInited;
        private static object s_lock = new object();
        private static string s_dbId;
        private static string s_collectionId;
        private static int s_offerThroughput;
        private static bool s_compressionEnabled;
        private static int s_timeout;
        private static Uri s_documentCollectionUri;
        private static IDocumentClient s_client;
        private static IndexingPolicy s_indexNone = new IndexingPolicy()
        {
            IndexingMode = IndexingMode.Consistent,
            ExcludedPaths = new System.Collections.ObjectModel.Collection<ExcludedPath>()
                            {
                                new ExcludedPath() { Path = "/*" }
                            },
            IncludedPaths = new System.Collections.ObjectModel.Collection<IncludedPath>()
        };

        #region CosmosDB Stored Procedures            
        private const string CreateSessionStateItemSPID = "CreateSessionStateItem";
        private const string CreateSessionStateItemInPartitionSPID = "CreateSessionStateItemInPartition";
        private const string GetStateItemSPID = "GetStateItem";
        private const string GetStateItemExclusiveSPID = "GetStateItemExclusive";
        private const string ReleaseItemExclusiveSPID = "ReleaseItemExclusive";
        private const string RemoveStateItemSPID = "RemoveStateItem";
        private const string ResetItemTimeoutSPID = "ResetItemTimeout";
        private const string UpdateSessionStateItemSPID = "UpdateSessionStateItem";

        private const string CreateSessionStateItemSP = @"
            function CreateSessionStateItem(sessionId, timeout, lockCookie, sessionItem, uninitialized) {
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
                            if (err) {
                                throw err;
                            }
                            response.setBody({documentCreated: documentCreated});
                });
            }";

        // Will be used in String.Format, hence needs to escape certain char
        private const string CreateSessionStateItemInPartitionSPTemplate = @"
            function CreateSessionStateItemInPartition(sessionId, partitionKeyValue, timeout, lockCookie, sessionItem, uninitialized) {{
                var collection = getContext().getCollection();
                var collectionLink = collection.getSelfLink();
                var response = getContext().getResponse();

                if (!sessionId) {{
                    throw new Error('sessionId cannot be null');
                }}
                if (!partitionKeyValue) {{
                    throw new Error('{0} cannot be null');
                }}
                if (!timeout) {{
                    throw new Error('timeout cannot be null');
                }}
                if (!lockCookie) {{
                    throw new Error('lockCookie cannot be null');
                }}

                var sessionStateItem = {{ id: sessionId, {0}: partitionKeyValue, lockDate: (new Date()).getTime(), lockAge: 0, lockCookie:lockCookie, 
                    ttl: timeout, locked: false, sessionItem: sessionItem, uninitialized: uninitialized }};
                collection.createDocument(collectionLink, sessionStateItem,
                    function (err, documentCreated) {{
                            if (err) {{
                                throw err;
                            }}
                            response.setBody({{documentCreated: documentCreated}});
                }});
            }}";


        private const string GetStateItemSP = @"
            function GetStateItem(sessionId) {
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
                    var isAccepted = collection.queryDocuments(collectionLink, query, continuation,
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
            function RemoveStateItem(sessionId, lockCookie) {
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
                    var isAccepted = collection.queryDocuments(collectionLink, query, continuation,
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
                        if (PartitionEnabled)
                        {
                            CreateSessionStateItemInPartitionSP = string.Format(CreateSessionStateItemInPartitionSPTemplate, s_partitionKey);
                        }

                        s_client = DocumentClientFactory(new Uri(s_endPoint), s_authKey, ParseCosmosDBClientSettings(providerConfig));

                        // setup CosmosDB
                        CreateDatabaseIfNotExistsAsync().Wait();
                        CreateCollectionIfNotExistsAsync().Wait();
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
            s_partitionKey = "";
            s_partitionNumUsedBySessionProvider = 0;
            s_dbId = "";
            s_collectionId = "";
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

        internal static string PartitionKey
        {
            get { return s_partitionKey; }
        }

        internal static string DbId
        {
            get { return s_dbId; }
        }

        internal static int PartitionNum
        {
            get { return s_partitionNumUsedBySessionProvider; }
        }

        internal static string CollectionId
        {
            get { return s_collectionId; }
        }

        internal static int ThroughPut
        {
            get { return s_offerThroughput; }
        }

        internal static IDocumentClient Client
        {
            get { return s_client; }
            set { s_client = value; }
        }

        internal static Func<HttpContext, HttpStaticObjectsCollection> GetSessionStaticObjects
        {
            get; set;
        } = SessionStateUtility.GetSessionStaticObjects;

        internal Func<Uri, string, ConnectionPolicy, IDocumentClient> DocumentClientFactory
        {
            get; set;
        } = (endpoint, authKey, policy) => new DocumentClient(endpoint, authKey, policy);
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
            var spLink = UriFactory.CreateStoredProcedureUri(s_dbId, s_collectionId, spName);
            var spResponse = await ExecuteStoredProcedureWithWrapperAsync<SessionStateItem>(spLink, CreateRequestOptions(id), id);

            CheckSPResponseAndThrowIfNeeded(spResponse);

            var sessionStateItem = spResponse.Response;
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
            var spLink = UriFactory.CreateStoredProcedureUri(s_dbId, s_collectionId, ReleaseItemExclusiveSPID);

            var spResponse = await ExecuteStoredProcedureWithWrapperAsync<object>(spLink, CreateRequestOptions(id), id, (int)lockId);

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
            var spLink = UriFactory.CreateStoredProcedureUri(s_dbId, s_collectionId, RemoveStateItemSPID);
            var spResponse = await ExecuteStoredProcedureWithWrapperAsync<object>(spLink, CreateRequestOptions(id), id, (int)lockId);

            CheckSPResponseAndThrowIfNeeded(spResponse);
        }

        /// <inheritdoc />
        public override async Task ResetItemTimeoutAsync(HttpContextBase context, string id, CancellationToken cancellationToken)
        {
            var spLink = UriFactory.CreateStoredProcedureUri(s_dbId, s_collectionId, ResetItemTimeoutSPID);
            var spResponse = await ExecuteStoredProcedureWithWrapperAsync<object>(spLink, CreateRequestOptions(id), id);

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
                var spLink = UriFactory.CreateStoredProcedureUri(s_dbId, s_collectionId, UpdateSessionStateItemSPID);
                var spResponse = await ExecuteStoredProcedureWithWrapperAsync<object>(spLink, CreateRequestOptions(id),
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

        internal string CreateSessionStateItemInPartitionSP { get; set; }

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

            s_collectionId = providerConfig["collectionId"];
            if (string.IsNullOrEmpty(s_collectionId))
            {
                throw new ConfigurationErrorsException(string.Format(SR.EmptyConfig_WithName, "collectionId"));
            }

            if (!int.TryParse(providerConfig["offerThroughput"], out s_offerThroughput))
            {
                s_offerThroughput = 5000;
            }
            
            s_partitionKey = providerConfig["partitionKeyPath"];

            if (PartitionEnabled)
            {
                if (providerConfig["partitionNumUsedByProvider"] == "*")
                {
                    s_partitionNumUsedBySessionProvider = -1;
                }
                else if (!int.TryParse(providerConfig["partitionNumUsedByProvider"], out s_partitionNumUsedBySessionProvider) || s_partitionNumUsedBySessionProvider < 1)
                {
                    s_partitionNumUsedBySessionProvider = 10;
                }

                // If the value of 'id' and the partition key are the same, it is ok to combine them. Since we're not doing wildcard partitioning
                // the values will not be the same. So we should throw if trying to use 'id' as the partition key. It will cause problems.
                if (s_partitionNumUsedBySessionProvider >= 0 && s_partitionKey == "id")
                {
                    throw new ConfigurationErrorsException(string.Format(SR.Cant_use_id_for_partition_key, "s_partitionKey"));
                }
            }
        }

        internal static ConnectionPolicy ParseCosmosDBClientSettings(NameValueCollection config)
        {
            ConnectionMode conMode;
            if (!Enum.TryParse<ConnectionMode>(config["connectionMode"], out conMode))
            {
                conMode = ConnectionMode.Direct;
            }

            Protocol conProtocol;
            if (!Enum.TryParse<Protocol>(config["connectionProtocol"], out conProtocol))
            {
                conProtocol = Protocol.Tcp;
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

            var connectionPolicy = new ConnectionPolicy
            {
                ConnectionMode = conMode,
                ConnectionProtocol = conProtocol,
                RequestTimeout = new TimeSpan(0, 0, requestTimeout),
                MaxConnectionLimit = maxConnectionLimit,
                EnableEndpointDiscovery = false,
                RetryOptions = new RetryOptions
                {
                    MaxRetryAttemptsOnThrottledRequests = maxRetryAttemptsOnThrottledRequests,
                    MaxRetryWaitTimeInSeconds = maxRetryWaitTimeInSeconds
                }
            };

            var preferredLocations = config["preferredLocations"];
            if (!string.IsNullOrEmpty(preferredLocations))
            {                
                foreach(var location in preferredLocations.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    connectionPolicy.PreferredLocations.Add(location);
                }
                if(connectionPolicy.PreferredLocations.Count > 0)
                {
                    connectionPolicy.EnableEndpointDiscovery = true;
                }
            }

            return connectionPolicy;
        }

        private static RequestOptions CreateRequestOptions(string sessionId)
        {
            if (PartitionEnabled)
            {
                return new RequestOptions
                {
                    PartitionKey = new PartitionKey(CreatePartitionValue(sessionId))
                };
            }
            else
            {
                return new RequestOptions();
            }
        }

        private static string CreatePartitionValue(string sessionId)
        {
            // If s_partitionNumUsedBySessionProvider is less than 0, that means the wildcard option was specified.
            // In which case, we are not limiting partition count. Just use the entire sessionId.
            if (s_partitionNumUsedBySessionProvider < 0)
                return sessionId;

            Debug.Assert(!string.IsNullOrEmpty(sessionId));
            Debug.Assert(PartitionEnabled);
            Debug.Assert(s_partitionNumUsedBySessionProvider != 0);

            // Default SessionIdManager uses all the 26 letters and number 0~5
            // which can create 32 different partitions
            return (sessionId[0] % s_partitionNumUsedBySessionProvider).ToString();
        }

        private static Uri DocumentCollectionUri
        {
            get {
                if (s_documentCollectionUri == null)
                {
                    s_documentCollectionUri = UriFactory.CreateDocumentCollectionUri(s_dbId, s_collectionId);
                }
                return s_documentCollectionUri;
            }
        }        

        private static async Task CreateDatabaseIfNotExistsAsync()
        {
            try
            {
                await s_client.ReadDatabaseAsync(UriFactory.CreateDatabaseUri(s_dbId));
            }
            catch (DocumentClientException e)
            {
                if (e.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    await s_client.CreateDatabaseAsync(new Database { Id = s_dbId });
                }
                else
                {
                    throw;
                }
            }
        }

        private static async Task CreateCollectionIfNotExistsAsync()
        {
            try
            {
                DocumentCollection dc = await s_client.ReadDocumentCollectionAsync(DocumentCollectionUri);

                // If we're using the updated partition strategy (see comment below) but we haven't updated our indexing policy - do that now.
                if (PartitionEnabled && (s_partitionNumUsedBySessionProvider < 0))
                {
                    // Contains doesn't work, presumably because a 'new ExcludePath' is a different object from the existing one, even if the path strings
                    // are exactly the same. So we do this the old-fashioned way.
                    bool containsWildcard = false;
                    foreach (var epath in dc.IndexingPolicy.ExcludedPaths)
                        if (epath.Path.Equals("/*", StringComparison.InvariantCultureIgnoreCase))
                            containsWildcard = true;

                    if (!containsWildcard)
                    {
                        dc.IndexingPolicy = s_indexNone;
                        await s_client.ReplaceDocumentCollectionAsync(DocumentCollectionUri, dc, new RequestOptions { OfferThroughput = s_offerThroughput });
                    }
                }
            }
            catch (DocumentClientException e)
            {
                if (e.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    var docCollection = new DocumentCollection
                    {
                        Id = s_collectionId,
                        DefaultTimeToLive = s_timeout
                    };

                    if(PartitionEnabled)
                    {
                        docCollection.PartitionKey.Paths.Add($"/{s_partitionKey}");

                        // If we're using the updated partition strategy - 1 logical partition per sessionId - then there is no
                        // need for indexing. It will not speed up lookup, and it only contributes to overhead on writes.
                        if (s_partitionNumUsedBySessionProvider < 0)
                            docCollection.IndexingPolicy = s_indexNone;
                    }

                    await s_client.CreateDocumentCollectionAsync(
                        UriFactory.CreateDatabaseUri(s_dbId),
                        docCollection,
                        new RequestOptions { OfferThroughput = s_offerThroughput });
                }
                else
                {
                    throw;
                }
            }
        }

        private static bool PartitionEnabled {
            get {
                return !string.IsNullOrEmpty(s_partitionKey);
            }
        }
        
        private async Task CreateStoredProceduresIfNotExistsAsync()
        {
            if (!PartitionEnabled)
            {
                await CreateSPIfNotExistsAsync(CreateSessionStateItemSPID, CreateSessionStateItemSP);
            }
            else
            {
                await CreateSPIfNotExistsAsync(CreateSessionStateItemInPartitionSPID, CreateSessionStateItemInPartitionSP);
            }
            await CreateSPIfNotExistsAsync(GetStateItemSPID, GetStateItemSP);
            await CreateSPIfNotExistsAsync(GetStateItemExclusiveSPID, GetStateItemExclusiveSP);
            await CreateSPIfNotExistsAsync(ReleaseItemExclusiveSPID, ReleaseItemExclusiveSP);
            await CreateSPIfNotExistsAsync(RemoveStateItemSPID, RemoveStateItemSP);
            await CreateSPIfNotExistsAsync(ResetItemTimeoutSPID, ResetItemTimeoutSP);
            await CreateSPIfNotExistsAsync(UpdateSessionStateItemSPID, UpdateSessionStateItemSP);
        }

        private static async Task CreateSPIfNotExistsAsync(string spId, string spBody)
        {
            try
            {
                var spLink = UriFactory.CreateStoredProcedureUri(s_dbId, s_collectionId, spId);
                await s_client.ReadStoredProcedureAsync(spLink);
            }
            catch (DocumentClientException e)
            {
                if (e.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    var sp = new StoredProcedure();
                    sp.Id = spId;
                    sp.Body = spBody;

                    await s_client.CreateStoredProcedureAsync(DocumentCollectionUri, sp);
                }
                else
                {
                    throw;
                }
            }
        }

        internal static async Task<IStoredProcedureResponse<TValue>> ExecuteStoredProcedureWithWrapperAsync<TValue>(
            Uri spLink, 
            RequestOptions requestOptions, 
            params dynamic[] spParams)
        {
            try
            {
                return await s_client.ExecuteStoredProcedureAsync<TValue>(spLink, requestOptions, spParams);
            }
            catch (DocumentClientException dce)
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
                var innerException = ae.InnerException as DocumentClientException;
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
            if (PartitionEnabled)
            {
                var spLink = UriFactory.CreateStoredProcedureUri(s_dbId, s_collectionId, CreateSessionStateItemInPartitionSPID);
                var spResponse = await ExecuteStoredProcedureWithWrapperAsync<object>(spLink, CreateRequestOptions(sessionid),
                    // sessionId, partitionValue, timeout, lockCookie, sessionItem, uninitialized
                    sessionid, CreatePartitionValue(sessionid), timeoutInSec, DefaultLockCookie, encodedSsItems, uninitialized);

                CheckSPResponseAndThrowIfNeeded(spResponse);
            }
            else
            {
                var spLink = UriFactory.CreateStoredProcedureUri(s_dbId, s_collectionId, CreateSessionStateItemSPID);
                var spResponse = await ExecuteStoredProcedureWithWrapperAsync<object>(spLink, CreateRequestOptions(sessionid),
                    // sessionId, timeout, lockCookie, sessionItem, uninitialized
                    sessionid, timeoutInSec, DefaultLockCookie, encodedSsItems, uninitialized);

                CheckSPResponseAndThrowIfNeeded(spResponse);
            }
        }

        private static void CheckSPResponseAndThrowIfNeeded<T>(IStoredProcedureResponse<T> spResponse)
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
