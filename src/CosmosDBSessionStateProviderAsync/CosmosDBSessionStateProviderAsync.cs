// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNet.SessionState
{
    using Microsoft.AspNet.SessionStateCosmosDBSessionStateProviderAsync.Resources;
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
        private static readonly int DefaultItemLength = 7000;
        private static readonly int DefaultLockCookie = 1;
        private static readonly string PartitionKeyPath = "/appId";

        private static bool s_oneTimeInited;
        private static object s_lock = new object();
        private static string s_appId;
        private static string s_dbName;
        private static string s_collectionID;
        private static int s_offerThroughput = 1000;
        private static bool s_compressionEnabled;
        private static int s_timeout;
        private static Uri s_documentCollectionUri;
        private static PartitionKey s_partitionKey;
        private static IDocumentClient s_client;

        #region CosmosDB Stored Procedures            
        private static readonly string CreateSessionStateItemSPID = "CreateUninitializedItem";
        private static readonly string GetStateItemSPID = "GetStateItem";
        private static readonly string GetStateItemExclusiveSPID = "GetStateItemExclusive";
        private static readonly string ReleaseItemExclusiveSPID = "ReleaseItemExclusive";
        private static readonly string RemoveStateItemSPID = "RemoveStateItem";
        private static readonly string ResetItemTimeoutSPID = "ResetItemTimeout";
        private static readonly string UpdateSessionStateItemSPID = "UpdateSessionStateItem";

        private static readonly string CreateSessionStateItemSP = @"
            function CreateUninitializedItem(sessionId, appId, timeout, lockCookie, sessionItem, uninitialized) {
                var collection = getContext().getCollection();
                var collectionLink = collection.getSelfLink();
                var response = getContext().getResponse();

                if (!sessionId) {
                    throw new Error('sessionId cannot be null');
                }
                if (!appId) {
                    throw new Error('appId cannot be null');
                }
                if (!timeout) {
                    throw new Error('timeout cannot be null');
                }
                if (!lockCookie) {
                    throw new Error('lockCookie cannot be null');
                }

                var sessionStateItem = { id: sessionId, appId: appId, lockDate: (new Date()).getTime(), lockAge: 0, lockCookie:lockCookie, 
                    ttl: timeout, locked: false, sessionItem: sessionItem, uninitialized: uninitialized };
                collection.createDocument(collectionLink, sessionStateItem,
                    function (err, documentCreated) {
                            if (err) {
                                throw err;
                            }
                            response.setBody({documentCreated: documentCreated});
                });
            }";

        private static readonly string GetStateItemSP = @"
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
                        function(err, documents, responseOptions)
                {
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
                                var responseDoc = { id: doc.id, appId: null, lockAge: null, lockCookie: null, ttl: null, 
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
                        var responseDoc = { id: '', appId: null, lockAge: null, lockCookie: null, ttl: null, 
                                            locked: false, sessionItem: null, uninitialized: null };
                        response.setBody(responseDoc);
                    }
                });

                    if (!isAccepted) {
                        throw new Error('The SP timed out.');
            }
                }
            }";

        private static readonly string GetStateItemExclusiveSP = @"
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
                        function(err, documents, responseOptions)
                {
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
                            var responseDoc = { id: doc.id, appId: null, lockAge: null, lockCookie: null, ttl: null, 
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
                            var responseDoc = { id: doc.id, appId: doc.appId, lockAge: doc.lockAge, lockCookie: doc.lockCookie + 1, ttl: doc.ttl,
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
                        var responseDoc = { id: '', appId: null, lockAge: null, lockCookie: null, ttl: null, 
                                            locked: false, sessionItem: null, uninitialized: null };
                        response.setBody(responseDoc);
                    }
                });

                    if (!isAccepted) {
                        throw new Error('The SP timed out.');
            }
                }
            }";

        private static readonly string ReleaseItemExclusiveSP = @"
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
                        function(err, documents, responseOptions)
                {
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

        private static readonly string RemoveStateItemSP = @"
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
                        function(err, documents, responseOptions)
                {
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

        private static readonly string ResetItemTimeoutSP = @"
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
                        function(err, documents, responseOptions)
                {
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

        private static readonly string UpdateSessionStateItemSP = @"
            function UpdateSessionStateItem(sessionId, lockCookie, sessionItem) {
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
                        function(err, documents, responseOptions)
                {
                    if (err)
                    {
                        throw err;
                    }
                    if (documents.length > 0)
                    {
                        var doc = documents[0];
                        doc.sessionItem = sessionItem;
                        doc.locked = false;
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

            if (!s_oneTimeInited)
            {
                lock (s_lock)
                {
                    if (!s_oneTimeInited)
                    {
                        SessionStateSection ssc = (SessionStateSection)ConfigurationManager.GetSection("system.web/sessionState");
                        s_compressionEnabled = ssc.CompressionEnabled;
                        s_timeout = (int)ssc.Timeout.TotalSeconds;

                        string endPoint;
                        string authKey;
                        ParseCosmosDbEndPointSettings(config, out endPoint, out authKey, out s_dbName, out s_collectionID, out s_offerThroughput);

                        s_client = new DocumentClient(new Uri(endPoint), authKey, ParseCosmosDBClientSettings(config));
                        s_appId = HttpRuntime.AppDomainAppId.GetHashCode().ToString("X8", CultureInfo.InvariantCulture);
                        s_partitionKey = new PartitionKey(s_appId);

                        // setup CosmosDB
                        CreateDatabaseIfNotExistsAsync().Wait();
                        CreateCollectionIfNotExistsAsync().Wait();
                        CreateStoredProceduresIfNotExistsAsync().Wait();
                       
                        s_oneTimeInited = true;
                    }
                }
            }
        }        

        /// <inheritdoc />
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

            byte[] buf;

            var item = new SessionStateStoreData(new SessionStateItemCollection(),
                        SessionStateUtility.GetSessionStaticObjects(context.ApplicationInstance.Context),
                        timeout);

            SerializeStoreData(item, DefaultItemLength, out buf);

            var spLink = UriFactory.CreateStoredProcedureUri(s_dbName, s_collectionID, CreateSessionStateItemSPID);
            var spResponse = await s_client.ExecuteStoredProcedureAsync<object>(spLink, CreateRequestOptions(),
                // sessionId, appId, timeout, lockCookie, sessionItem, uninitialized
                id, s_appId, timeout, DefaultLockCookie, buf, true);

            CheckSPResponseAndThrowIfNeeded(spResponse);
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
            var spLink = UriFactory.CreateStoredProcedureUri(s_dbName, s_collectionID, spName);
            var spResponse = await s_client.ExecuteStoredProcedureAsync<SessionStateItem>(spLink, CreateRequestOptions(), id);

            CheckSPResponseAndThrowIfNeeded(spResponse);

            var sessionStateItem = spResponse.Response;
            if (string.IsNullOrEmpty(sessionStateItem.AppId))
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
                    return new GetItemResult(data, sessionStateItem.Locked.Value, sessionStateItem.LockAge.Value, sessionStateItem.LockCookie.Value, SessionStateActions.None);
                }
            }
        }

        /// <inheritdoc />
        public override void InitializeRequest(HttpContextBase context) { }

        /// <inheritdoc />
        public override async Task ReleaseItemExclusiveAsync(HttpContextBase context, string id, object lockId, CancellationToken cancellationToken)
        {
            var spLink = UriFactory.CreateStoredProcedureUri(s_dbName, s_collectionID, ReleaseItemExclusiveSPID);

            var spResponse = await s_client.ExecuteStoredProcedureAsync<object>(spLink, CreateRequestOptions(), id, (int)lockId);

            CheckSPResponseAndThrowIfNeeded(spResponse);
        }

        /// <inheritdoc />
        public override async Task RemoveItemAsync(HttpContextBase context, string id, object lockId, SessionStateStoreData item, CancellationToken cancellationToken)
        {
            var spLink = UriFactory.CreateStoredProcedureUri(s_dbName, s_collectionID, RemoveStateItemSPID);
            var spResponse = await s_client.ExecuteStoredProcedureAsync<object>(spLink, CreateRequestOptions(), id, (int)lockId);

            CheckSPResponseAndThrowIfNeeded(spResponse);
        }

        /// <inheritdoc />
        public override async Task ResetItemTimeoutAsync(HttpContextBase context, string id, CancellationToken cancellationToken)
        {
            var spLink = UriFactory.CreateStoredProcedureUri(s_dbName, s_collectionID, ResetItemTimeoutSPID);
            var spResponse = await s_client.ExecuteStoredProcedureAsync<object>(spLink, CreateRequestOptions(), id);

            CheckSPResponseAndThrowIfNeeded(spResponse);
        }

        /// <inheritdoc />
        public override async Task SetAndReleaseItemExclusiveAsync(HttpContextBase context, string id, SessionStateStoreData item, object lockId, bool newItem, CancellationToken cancellationToken)
        {
            byte[] buf;
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
                SerializeStoreData(item, DefaultItemLength, out buf);
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
                var spLink = UriFactory.CreateStoredProcedureUri(s_dbName, s_collectionID, CreateSessionStateItemSPID);
                var spResponse = await s_client.ExecuteStoredProcedureAsync<object>(spLink, CreateRequestOptions(),
                    //sessionId, appId, timeout, lockCookie, sessionItem, uninitialized
                    id, s_appId, s_timeout, lockCookie, buf, false);

                CheckSPResponseAndThrowIfNeeded(spResponse);
            }
            else
            {
                var spLink = UriFactory.CreateStoredProcedureUri(s_dbName, s_collectionID, UpdateSessionStateItemSPID);
                var spResponse = await s_client.ExecuteStoredProcedureAsync<object>(spLink, CreateRequestOptions(),
                    //sessionId, lockCookie, sessionItem
                    id, lockCookie, buf);

                CheckSPResponseAndThrowIfNeeded(spResponse);
            }
        }

        /// <inheritdoc />
        public override bool SetItemExpireCallback(SessionStateItemExpireCallback expireCallback)
        {
            return false;
        }

        private static void ParseCosmosDbEndPointSettings(NameValueCollection config,
            out string endPoint, out string authKey, out string dbId, out string collectionId, out int offerThroughPut)
        {
            endPoint = config["endPoint"];
            if (string.IsNullOrEmpty(endPoint))
            {
                throw new ConfigurationErrorsException(string.Format(SR.EmptyConfig_WithName, "endPoint"));
            }

            authKey = config["authKey"];
            if (string.IsNullOrEmpty(authKey))
            {
                throw new ConfigurationErrorsException(string.Format(SR.EmptyConfig_WithName, "authKey"));
            }

            dbId = config["databaseId"];
            if (string.IsNullOrEmpty(dbId))
            {
                throw new ConfigurationErrorsException(string.Format(SR.EmptyConfig_WithName, "databaseId"));
            }

            collectionId = config["collectionId"];
            if (string.IsNullOrEmpty(collectionId))
            {
                throw new ConfigurationErrorsException(string.Format(SR.EmptyConfig_WithName, "collectionId"));
            }

            if (!int.TryParse(config["offerThroughput"], out offerThroughPut))
            {
                offerThroughPut = 5000;
            }
        }

        private static ConnectionPolicy ParseCosmosDBClientSettings(NameValueCollection config)
        {
            ConnectionMode conMode;
            if (!Enum.TryParse<ConnectionMode>(config["connectionMode"], out conMode))
            {
                conMode = ConnectionMode.Direct;
            }

            Protocol conProtocol;
            if (!Enum.TryParse<Protocol>(config["connectionProtocol"], out conProtocol))
            {
                conProtocol = Protocol.Https;
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

            return new ConnectionPolicy
            {
                ConnectionMode = conMode,
                ConnectionProtocol = conProtocol,
                RequestTimeout = new TimeSpan(0, 0, requestTimeout),
                MaxConnectionLimit = maxConnectionLimit,
                RetryOptions = new RetryOptions
                {
                    MaxRetryAttemptsOnThrottledRequests = maxRetryAttemptsOnThrottledRequests,
                    MaxRetryWaitTimeInSeconds = maxRetryWaitTimeInSeconds
                }
            };
        }

        private static RequestOptions CreateRequestOptions()
        {
            return new RequestOptions
            {
                PartitionKey = s_partitionKey
            };
        }

        private static Uri DocumentCollectionUri
        {
            get {
                if (s_documentCollectionUri == null)
                {
                    s_documentCollectionUri = UriFactory.CreateDocumentCollectionUri(s_dbName, s_collectionID);
                }
                return s_documentCollectionUri;
            }
        }        

        private static async Task CreateDatabaseIfNotExistsAsync()
        {
            try
            {
                await s_client.ReadDatabaseAsync(UriFactory.CreateDatabaseUri(s_dbName));
            }
            catch (DocumentClientException e)
            {
                if (e.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    await s_client.CreateDatabaseAsync(new Database { Id = s_dbName });
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
                await s_client.ReadDocumentCollectionAsync(DocumentCollectionUri);
            }
            catch (DocumentClientException e)
            {
                if (e.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    var docCollection = new DocumentCollection
                    {
                        Id = s_collectionID,
                        DefaultTimeToLive = s_timeout
                    };
                    docCollection.PartitionKey.Paths.Add(PartitionKeyPath);

                    await s_client.CreateDocumentCollectionAsync(
                        UriFactory.CreateDatabaseUri(s_dbName),
                        docCollection,
                        new RequestOptions { OfferThroughput = s_offerThroughput });
                }
                else
                {
                    throw;
                }
            }
        }

        private static async Task CreateStoredProceduresIfNotExistsAsync()
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
            try
            {
                var spLink = UriFactory.CreateStoredProcedureUri(s_dbName, s_collectionID, spId);
                var sproc = await s_client.ReadStoredProcedureAsync(spLink);
            }
            catch (DocumentClientException e)
            {
                if (e.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    var sp = new StoredProcedure();
                    sp.Id = spId;
                    sp.Body = spBody;

                    await s_client.CreateStoredProcedureAsync(DocumentCollectionUri, sp, CreateRequestOptions());
                }
                else
                {
                    throw;
                }
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
        private static void SerializeStoreData(
            SessionStateStoreData item,
            int initialStreamSize,
            out byte[] buf)
        {
            using (MemoryStream s = new MemoryStream())
            {
                Serialize(item, s);
                if (s_compressionEnabled)
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
    }
}
