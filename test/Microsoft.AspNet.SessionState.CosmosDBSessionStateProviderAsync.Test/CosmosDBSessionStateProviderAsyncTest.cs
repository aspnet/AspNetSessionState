// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See the License.txt file in the project root for full license information.

namespace Microsoft.AspNet.SessionState.CosmosDBSessionStateAsyncProvider.Test
{
    using Microsoft.Azure.Documents;
    using Microsoft.Azure.Documents.Client;
    using Moq;
    using Newtonsoft.Json;
    using System;
    using System.Collections.Specialized;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http.Headers;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web;
    using System.Web.Configuration;
    using System.Web.SessionState;
    using Xunit;

    public class CosmosDBSessionStateProviderAsyncTest
    {
        private const string CreateSessionStateItemSPID = "CreateSessionStateItem";
        private const string CreateSessionStateItemInPartitionSPID = "CreateSessionStateItemInPartition";
        private const string GetStateItemSPID = "GetStateItem";
        private const string GetStateItemExclusiveSPID = "GetStateItemExclusive";
        private const string ReleaseItemExclusiveSPID = "ReleaseItemExclusive";
        private const string RemoveStateItemSPID = "RemoveStateItem";
        private const string ResetItemTimeoutSPID = "ResetItemTimeout";
        private const string UpdateSessionStateItemSPID = "UpdateSessionStateItem";

        private const string RequestTooLargeExceptionMsg = "The request rate to CosmosDB is too large. You may consider to increase the offer throughput of the CosmosDB collection or increase maxRetryAttemptsOnThrottledRequests and maxRetryWaitTimeInSeconds settings in web.config";
        private const string AppId = "TestWebsite";
        private const string DefaultProviderName = "CosmosDBSessionStateProviderAsync";
        private const string DefaultEndPointSettingKey = "cosmosDBEndPointSetting";
        private const string DefaultAuthKeySettingKey = "cosmosDBAuthKeySetting";
        private const string AnotherEndPointSettingKey = "AnotherEndPointSettingKey";
        private const string AnotherAuthKeySettingKey = "AnotherAuthKeySettingKey";
        private const string DatabaseId = "TestDatabase";
        private const string CollectionId = "TestCollection";
        private const string EndPoint = "https://test.documents.azure.com";
        private const string AuthKey = "[AuthKey]";
        private const string ParitionKeyPath = "SessionId";
        private const string TestSessionId = "piqhlifa30ooedcp1k42mtef";
        private const string DefaultPartitionValue = "12";   // The partition assigned to TestSessionId using default settings
        private const int DefaultLockCookie = 1;
        private const int AnotherLockCookie = 2;
        private const int PartitionNum = 20;
        private const int DefaultPartitionNum = 10;
        private const string WildcardPartitionString = "*";
        private const int WildcardPartitionCount = -1;
        private const int DefaultThroughPut = 5000;
        private const int DefaultSessionTimeout = 20;
        private const int DefaultSessionTimeoutInSec = 60 * 20;
        private const int AnotherSessionTimeout = 60 * 5;
        private const ConnectionMode DefaultConnectionMode = ConnectionMode.Direct;
        private const Protocol DefaultProtocal = Protocol.Tcp;
        private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(5);
        private const int DefaultMaxConnectionLimit = 50;
        private const int DefaultMaxRetryAttempts = 9;
        private const int DefaultMaxRetryWaitTime = 10;
        private const int DefaultItemLength = 7000;
        private static readonly Uri TestSPLink = new Uri("http://test.doc.sp");

        public CosmosDBSessionStateProviderAsyncTest()
        {
            CosmosDBSessionStateProviderAsync.GetSessionStaticObjects = cxt => new HttpStaticObjectsCollection();
        }

        [Fact]
        public void Initialize_Without_Existing_CosmosDBCollection_Should_Create_NewOne()
        {
            var provider = CreateProvider();
            
            var providerConfig = new NameValueCollection();
            var appSettings = new NameValueCollection();
            var ssc = new SessionStateSection();

            providerConfig["cosmosDBEndPointSettingKey"] = DefaultEndPointSettingKey;
            appSettings[DefaultEndPointSettingKey] = EndPoint;
            providerConfig["cosmosDBAuthKeySettingKey"] = DefaultAuthKeySettingKey;
            appSettings[DefaultAuthKeySettingKey] = AuthKey;
            providerConfig["databaseId"] = DatabaseId;
            providerConfig["collectionId"] = CollectionId;

            var docClientMoq = new Mock<IDocumentClient>();
            var notFoundClientException = CreateDocumentClientException();
            var docCollectionUri = UriFactory.CreateDocumentCollectionUri(DatabaseId, CollectionId);
            
            var dbCreated = false;
            var docCollectionCreated = false;
            var createSessionStateItemSPCreated = false;
            var getStateItemExclusiveSPCreated = false;
            var releaseItemExclusiveSPCreated = false;
            var removeStateItemSPCreated = false;
            var resetItemTimeoutSPCreated = false;
            var updateSessionStateItemSPCreated = false;

            var dbUri = UriFactory.CreateDatabaseUri(DatabaseId);
            docClientMoq.Setup(client => client.ReadDatabaseAsync(dbUri, null)).Throws(notFoundClientException);
            docClientMoq.Setup(client => client.CreateDatabaseAsync(It.Is<Database>(db => db.Id == DatabaseId), null))
                .Returns(Task.FromResult((ResourceResponse<Database>)null))
                .Callback(() => dbCreated = true);

            docClientMoq.Setup(client => client.ReadDocumentCollectionAsync(docCollectionUri, null))
                .Throws(notFoundClientException);
            docClientMoq.Setup(client => client.CreateDocumentCollectionAsync(dbUri, 
                    It.Is<DocumentCollection>(docCollection => docCollection.Id == CollectionId 
                                              && docCollection.DefaultTimeToLive == DefaultSessionTimeoutInSec), 
                    It.Is<RequestOptions>(option => option.OfferThroughput == DefaultThroughPut)))
                .Returns(Task.FromResult((ResourceResponse<DocumentCollection>)null))
                .Callback(() => docCollectionCreated = true);
 
            SetupReadSPFailureMock(docClientMoq, DatabaseId, CollectionId, CreateSessionStateItemSPID, notFoundClientException);
            SetupCreateSPMock(docClientMoq, docCollectionUri, CreateSessionStateItemSPID, () => createSessionStateItemSPCreated = true);

            SetupReadSPFailureMock(docClientMoq, DatabaseId, CollectionId, GetStateItemExclusiveSPID, notFoundClientException);
            SetupCreateSPMock(docClientMoq, docCollectionUri, GetStateItemExclusiveSPID, () => getStateItemExclusiveSPCreated = true);

            SetupReadSPFailureMock(docClientMoq, DatabaseId, CollectionId, ReleaseItemExclusiveSPID, notFoundClientException);
            SetupCreateSPMock(docClientMoq, docCollectionUri, ReleaseItemExclusiveSPID, () => releaseItemExclusiveSPCreated = true);

            SetupReadSPFailureMock(docClientMoq, DatabaseId, CollectionId, RemoveStateItemSPID, notFoundClientException);
            SetupCreateSPMock(docClientMoq, docCollectionUri, RemoveStateItemSPID, () => removeStateItemSPCreated = true);

            SetupReadSPFailureMock(docClientMoq, DatabaseId, CollectionId, ResetItemTimeoutSPID, notFoundClientException);
            SetupCreateSPMock(docClientMoq, docCollectionUri, ResetItemTimeoutSPID, () => resetItemTimeoutSPCreated = true);

            SetupReadSPFailureMock(docClientMoq, DatabaseId, CollectionId, UpdateSessionStateItemSPID, notFoundClientException);
            SetupCreateSPMock(docClientMoq, docCollectionUri, UpdateSessionStateItemSPID, () => updateSessionStateItemSPCreated = true);

            provider.DocumentClientFactory = (endpoint, authKey, policy) => docClientMoq.Object;

            provider.Initialize(DefaultProviderName, providerConfig, ssc, appSettings);

            Assert.Equal(DefaultSessionTimeoutInSec, provider.Timeout);
            Assert.False(provider.CompressionEnabled);
            Assert.True(dbCreated);
            Assert.True(docCollectionCreated);
            Assert.True(createSessionStateItemSPCreated);
            Assert.True(getStateItemExclusiveSPCreated);
            Assert.True(releaseItemExclusiveSPCreated);
            Assert.True(removeStateItemSPCreated);
            Assert.True(resetItemTimeoutSPCreated);
            Assert.True(updateSessionStateItemSPCreated);
        }

        [Fact]
        public void Initialize_With_Existing_CosmosDBCollection_Should_Reuse_It()
        {
            var provider = CreateProvider();

            var providerConfig = new NameValueCollection();
            var appSettings = new NameValueCollection();
            var ssc = new SessionStateSection();

            ssc.Timeout = TimeSpan.FromSeconds(AnotherSessionTimeout);
            ssc.CompressionEnabled = true;

            providerConfig["cosmosDBEndPointSettingKey"] = AnotherEndPointSettingKey;
            appSettings[AnotherEndPointSettingKey] = EndPoint;
            providerConfig["cosmosDBAuthKeySettingKey"] = AnotherAuthKeySettingKey;
            appSettings[AnotherAuthKeySettingKey] = AuthKey;
            providerConfig["databaseId"] = DatabaseId;
            providerConfig["collectionId"] = CollectionId;
            providerConfig["partitionKeyPath"] = ParitionKeyPath;
            providerConfig["partitionNumUsedByProvider"] = PartitionNum.ToString();
            providerConfig["connectionMode"] = "Gateway";
            providerConfig["connectionProtocol"] = "Https";
            providerConfig["requestTimeout"] = "1";
            providerConfig["maxConnectionLimit"] = "10";
            providerConfig["maxRetryAttemptsOnThrottledRequests"] = "11";
            providerConfig["maxRetryWaitTimeInSeconds"] = "5";
            providerConfig["preferredLocations"] = "West US;Japan West";

            var docClientMoq = new Mock<IDocumentClient>();
            var notFoundClientException = CreateDocumentClientException();
            var docCollectionUri = UriFactory.CreateDocumentCollectionUri(DatabaseId, CollectionId);

            var dbCreated = false;
            var dbRead = false;
            var docCollectionCreated = false;
            var docCollectionRead = false;
            var createSessionStateItemSPCreated = false;
            var createSessionStateItemSPRead = false;
            var getStateItemExclusiveSPCreated = false;
            var getStateItemExclusiveSPRead = false;
            var releaseItemExclusiveSPCreated = false;
            var releaseItemExclusiveSPRead = false;
            var removeStateItemSPCreated = false;
            var removeStateItemSPRead = true;
            var resetItemTimeoutSPCreated = false;
            var resetItemTimeoutSPRead = false;
            var updateSessionStateItemSPCreated = false;
            var updateSessionStateItemSPRead = false;

            var dbUri = UriFactory.CreateDatabaseUri(DatabaseId);
            docClientMoq.Setup(client => client.ReadDatabaseAsync(dbUri, null))
                .Returns(Task.FromResult((ResourceResponse<Database>)null))
                .Callback(() => dbRead = true);
            docClientMoq.Setup(client => client.CreateDatabaseAsync(It.Is<Database>(db => db.Id == DatabaseId), null))
                .Returns(Task.FromResult((ResourceResponse<Database>)null))
                .Callback(() => dbCreated = true);

            SetupReadDocumentCollectionAsyncMock(docClientMoq, docCollectionUri, () => docCollectionRead = true);

            docClientMoq.Setup(client => client.CreateDocumentCollectionAsync(dbUri,
                    It.Is<DocumentCollection>(docCollection => docCollection.Id == CollectionId
                                              && docCollection.DefaultTimeToLive == DefaultSessionTimeoutInSec),
                    It.Is<RequestOptions>(option => option.OfferThroughput == DefaultThroughPut)))
                .Returns(Task.FromResult((ResourceResponse<DocumentCollection>)null))
                .Callback(() => docCollectionCreated = true);

            SetupReadSPSucessMock(docClientMoq, DatabaseId, CollectionId, CreateSessionStateItemInPartitionSPID, () => createSessionStateItemSPRead = true);            
            SetupCreateSPMock(docClientMoq, docCollectionUri, CreateSessionStateItemInPartitionSPID, () => createSessionStateItemSPCreated = true);

            SetupReadSPSucessMock(docClientMoq, DatabaseId, CollectionId, GetStateItemExclusiveSPID, () => getStateItemExclusiveSPRead = true);
            SetupCreateSPMock(docClientMoq, docCollectionUri, GetStateItemExclusiveSPID, () => getStateItemExclusiveSPCreated = true);

            SetupReadSPSucessMock(docClientMoq, DatabaseId, CollectionId, ReleaseItemExclusiveSPID, () => releaseItemExclusiveSPRead = true);
            SetupCreateSPMock(docClientMoq, docCollectionUri, ReleaseItemExclusiveSPID, () => releaseItemExclusiveSPCreated = true);

            SetupReadSPSucessMock(docClientMoq, DatabaseId, CollectionId, RemoveStateItemSPID, () => removeStateItemSPRead = true);
            SetupCreateSPMock(docClientMoq, docCollectionUri, RemoveStateItemSPID, () => removeStateItemSPCreated = true);

            SetupReadSPSucessMock(docClientMoq, DatabaseId, CollectionId, ResetItemTimeoutSPID, () => resetItemTimeoutSPRead = true);
            SetupCreateSPMock(docClientMoq, docCollectionUri, ResetItemTimeoutSPID, () => resetItemTimeoutSPCreated = true);

            SetupReadSPSucessMock(docClientMoq, DatabaseId, CollectionId, UpdateSessionStateItemSPID, () => updateSessionStateItemSPRead = true);
            SetupCreateSPMock(docClientMoq, docCollectionUri, UpdateSessionStateItemSPID, () => updateSessionStateItemSPCreated = true);

            ConnectionPolicy configuredPolicy = null;
            provider.DocumentClientFactory = (endpoint, authKey, policy) => { configuredPolicy = policy; return docClientMoq.Object; };

            provider.Initialize(DefaultProviderName, providerConfig, ssc, appSettings);

            Assert.Equal(EndPoint, CosmosDBSessionStateProviderAsync.EndPoint);
            Assert.Equal(AuthKey, CosmosDBSessionStateProviderAsync.AuthKey);
            Assert.Equal(DatabaseId, CosmosDBSessionStateProviderAsync.DbId);
            Assert.Equal(CollectionId, CosmosDBSessionStateProviderAsync.CollectionId);
            Assert.Equal(DefaultThroughPut, CosmosDBSessionStateProviderAsync.ThroughPut);
            Assert.Equal(ParitionKeyPath, CosmosDBSessionStateProviderAsync.PartitionKey);
            Assert.Equal(PartitionNum, CosmosDBSessionStateProviderAsync.PartitionNum);

            Assert.NotNull(configuredPolicy);
            Assert.Equal(ConnectionMode.Gateway, configuredPolicy.ConnectionMode);
            Assert.Equal(Protocol.Https, configuredPolicy.ConnectionProtocol);
            Assert.Equal(TimeSpan.FromSeconds(1), configuredPolicy.RequestTimeout);
            Assert.Equal(10, configuredPolicy.MaxConnectionLimit);
            Assert.Equal(11, configuredPolicy.RetryOptions.MaxRetryAttemptsOnThrottledRequests);
            Assert.Equal(5, configuredPolicy.RetryOptions.MaxRetryWaitTimeInSeconds);
            Assert.True(configuredPolicy.EnableEndpointDiscovery);
            Assert.Collection(configuredPolicy.PreferredLocations, loc => Assert.Equal("West US", loc), loc => Assert.Equal("Japan West", loc));

            Assert.Equal(AnotherSessionTimeout, provider.Timeout);
            Assert.True(provider.CompressionEnabled);
            Assert.True(dbRead);
            Assert.True(docCollectionRead);
            Assert.True(createSessionStateItemSPRead);
            Assert.True(getStateItemExclusiveSPRead);
            Assert.True(releaseItemExclusiveSPRead);
            Assert.True(removeStateItemSPRead);
            Assert.True(resetItemTimeoutSPRead);
            Assert.True(updateSessionStateItemSPRead);
            Assert.False(dbCreated);
            Assert.False(docCollectionCreated);
            Assert.False(createSessionStateItemSPCreated);
            Assert.False(getStateItemExclusiveSPCreated);
            Assert.False(releaseItemExclusiveSPCreated);
            Assert.False(removeStateItemSPCreated);
            Assert.False(resetItemTimeoutSPCreated);
            Assert.False(updateSessionStateItemSPCreated);
        }

        [Fact]
        public void CreateNewStoreData_Should_Return_Empty_Store()
        {
            var provider = CreateProvider();

            var providerConfig = new NameValueCollection();
            var appSettings = new NameValueCollection();
            var ssc = new SessionStateSection();

            providerConfig["cosmosDBEndPointSettingKey"] = DefaultEndPointSettingKey;
            appSettings[DefaultEndPointSettingKey] = EndPoint;
            providerConfig["cosmosDBAuthKeySettingKey"] = DefaultAuthKeySettingKey;
            appSettings[DefaultAuthKeySettingKey] = AuthKey;
            providerConfig["databaseId"] = DatabaseId;
            providerConfig["collectionId"] = CollectionId;

            var docClientMoq = new Mock<IDocumentClient>();
            SetupReadDocumentCollectionAsyncMock(docClientMoq, UriFactory.CreateDocumentCollectionUri(DatabaseId, CollectionId), () => { });
            provider.DocumentClientFactory = (endpoint, authKey, policy) => docClientMoq.Object;
            provider.Initialize(DefaultProviderName, providerConfig, ssc, appSettings);

            var store = provider.CreateNewStoreData(null, DefaultSessionTimeout);
            Assert.Equal(0, store.Items.Count);
            Assert.Null(store.StaticObjects);
            Assert.Equal(DefaultSessionTimeout, store.Timeout);
        }
        
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Serialize_And_Deserialized_SessionStateStoreData_RoundTrip_Should_Work(bool enableCompression)
        {
            var sessionCollection = new SessionStateItemCollection();
            var now = DateTime.UtcNow;
            sessionCollection["test1"] = "test1";
            sessionCollection["test2"] = now;
            var data = new SessionStateStoreData(sessionCollection, new HttpStaticObjectsCollection(), DefaultSessionTimeout);

            string buff;
            SessionStateStoreData deserializedData;

            CosmosDBSessionStateProviderAsync.SerializeStoreData(data, out buff, enableCompression);
            using (var stream = new MemoryStream(Convert.FromBase64String(buff)))
            {
                var httpContext = CreateMoqHttpContextBase();
                deserializedData = CosmosDBSessionStateProviderAsync.DeserializeStoreData(httpContext, stream, enableCompression);
            }

            Assert.Equal(data.Items.Count, deserializedData.Items.Count);
            Assert.Equal(data.Items["test1"], deserializedData.Items["test1"]);
            Assert.Equal(now, (DateTime)deserializedData.Items["test2"]);
            Assert.NotNull(deserializedData.StaticObjects);
            Assert.Equal(data.Timeout, deserializedData.Timeout);
        }
        
        [Fact]
        public void ParseCosmosDbEndPointSettings_Should_Initialize_EndPointSetting_With_Default_Config()
        {
            var providerConfig = new NameValueCollection();
            var appSettings = new NameValueCollection();

            providerConfig["cosmosDBEndPointSettingKey"] = DefaultEndPointSettingKey;
            appSettings[DefaultEndPointSettingKey] = EndPoint;
            providerConfig["cosmosDBAuthKeySettingKey"] = DefaultAuthKeySettingKey;
            appSettings[DefaultAuthKeySettingKey] = AuthKey;
            providerConfig["databaseId"] = DatabaseId;
            providerConfig["collectionId"] = CollectionId;

            CosmosDBSessionStateProviderAsync.ResetStaticFields();
            CosmosDBSessionStateProviderAsync.ParseCosmosDbEndPointSettings(providerConfig, appSettings);

            Assert.Equal(EndPoint, CosmosDBSessionStateProviderAsync.EndPoint);
            Assert.Equal(AuthKey, CosmosDBSessionStateProviderAsync.AuthKey);
            Assert.Equal(DatabaseId, CosmosDBSessionStateProviderAsync.DbId);
            Assert.Equal(CollectionId, CosmosDBSessionStateProviderAsync.CollectionId);
            Assert.Equal(DefaultThroughPut, CosmosDBSessionStateProviderAsync.ThroughPut);
            Assert.Equal(0, CosmosDBSessionStateProviderAsync.PartitionNum);
            Assert.Null(CosmosDBSessionStateProviderAsync.PartitionKey);
        }

        [Fact]
        public void ParseCosmosDbEndPointSettings_Should_Initialize_EndPointSetting_With_Custom_Config()
        {
            var providerConfig = new NameValueCollection();
            var appSettings = new NameValueCollection();

            providerConfig["cosmosDBEndPointSettingKey"] = DefaultEndPointSettingKey;
            appSettings[DefaultEndPointSettingKey] = EndPoint;
            providerConfig["cosmosDBAuthKeySettingKey"] = DefaultAuthKeySettingKey;
            appSettings[DefaultAuthKeySettingKey] = AuthKey;
            providerConfig["databaseId"] = DatabaseId;
            providerConfig["collectionId"] = CollectionId;
            providerConfig["partitionKeyPath"] = ParitionKeyPath;
            providerConfig["partitionNumUsedByProvider"] = PartitionNum.ToString();

            CosmosDBSessionStateProviderAsync.ResetStaticFields();
            CosmosDBSessionStateProviderAsync.ParseCosmosDbEndPointSettings(providerConfig, appSettings);

            Assert.Equal(EndPoint, CosmosDBSessionStateProviderAsync.EndPoint);
            Assert.Equal(AuthKey, CosmosDBSessionStateProviderAsync.AuthKey);
            Assert.Equal(DatabaseId, CosmosDBSessionStateProviderAsync.DbId);
            Assert.Equal(CollectionId, CosmosDBSessionStateProviderAsync.CollectionId);
            Assert.Equal(DefaultThroughPut, CosmosDBSessionStateProviderAsync.ThroughPut);
            Assert.Equal(ParitionKeyPath, CosmosDBSessionStateProviderAsync.PartitionKey);
            Assert.Equal(PartitionNum, CosmosDBSessionStateProviderAsync.PartitionNum);
        }

        [Fact]
        public void ParseCosmosDbEndPointSettings_Should_Initialize_EndPointSetting_With_No_PartitionNum()
        {
            var providerConfig = new NameValueCollection();
            var appSettings = new NameValueCollection();

            providerConfig["cosmosDBEndPointSettingKey"] = DefaultEndPointSettingKey;
            appSettings[DefaultEndPointSettingKey] = EndPoint;
            providerConfig["cosmosDBAuthKeySettingKey"] = DefaultAuthKeySettingKey;
            appSettings[DefaultAuthKeySettingKey] = AuthKey;
            providerConfig["databaseId"] = DatabaseId;
            providerConfig["collectionId"] = CollectionId;
            providerConfig["partitionKeyPath"] = ParitionKeyPath;

            CosmosDBSessionStateProviderAsync.ResetStaticFields();
            CosmosDBSessionStateProviderAsync.ParseCosmosDbEndPointSettings(providerConfig, appSettings);

            Assert.Equal(EndPoint, CosmosDBSessionStateProviderAsync.EndPoint);
            Assert.Equal(AuthKey, CosmosDBSessionStateProviderAsync.AuthKey);
            Assert.Equal(DatabaseId, CosmosDBSessionStateProviderAsync.DbId);
            Assert.Equal(CollectionId, CosmosDBSessionStateProviderAsync.CollectionId);
            Assert.Equal(DefaultThroughPut, CosmosDBSessionStateProviderAsync.ThroughPut);
            Assert.Equal(ParitionKeyPath, CosmosDBSessionStateProviderAsync.PartitionKey);
            Assert.Equal(DefaultPartitionNum, CosmosDBSessionStateProviderAsync.PartitionNum);
        }

        [Fact]
        public void ParseCosmosDbEndPointSettings_Should_Initialize_EndPointSetting_With_Wildcard_PartitionNum()
        {
            var providerConfig = new NameValueCollection();
            var appSettings = new NameValueCollection();

            providerConfig["cosmosDBEndPointSettingKey"] = DefaultEndPointSettingKey;
            appSettings[DefaultEndPointSettingKey] = EndPoint;
            providerConfig["cosmosDBAuthKeySettingKey"] = DefaultAuthKeySettingKey;
            appSettings[DefaultAuthKeySettingKey] = AuthKey;
            providerConfig["databaseId"] = DatabaseId;
            providerConfig["collectionId"] = CollectionId;
            providerConfig["partitionKeyPath"] = ParitionKeyPath;
            providerConfig["partitionNumUsedByProvider"] = WildcardPartitionString;

            CosmosDBSessionStateProviderAsync.ResetStaticFields();
            CosmosDBSessionStateProviderAsync.ParseCosmosDbEndPointSettings(providerConfig, appSettings);

            Assert.Equal(EndPoint, CosmosDBSessionStateProviderAsync.EndPoint);
            Assert.Equal(AuthKey, CosmosDBSessionStateProviderAsync.AuthKey);
            Assert.Equal(DatabaseId, CosmosDBSessionStateProviderAsync.DbId);
            Assert.Equal(CollectionId, CosmosDBSessionStateProviderAsync.CollectionId);
            Assert.Equal(DefaultThroughPut, CosmosDBSessionStateProviderAsync.ThroughPut);
            Assert.Equal(ParitionKeyPath, CosmosDBSessionStateProviderAsync.PartitionKey);
            Assert.Equal(WildcardPartitionCount, CosmosDBSessionStateProviderAsync.PartitionNum);
        }

        [Fact]
        public void ParseCosmosDBClientSettings_Should_Work_Without_Config()
        {
            var config = new NameValueCollection();

            CosmosDBSessionStateProviderAsync.ResetStaticFields();
            var policy = CosmosDBSessionStateProviderAsync.ParseCosmosDBClientSettings(config);

            Assert.Equal(DefaultConnectionMode, policy.ConnectionMode);
            Assert.Equal(DefaultProtocal, policy.ConnectionProtocol);
            Assert.Equal(DefaultRequestTimeout, policy.RequestTimeout);
            Assert.Equal(DefaultMaxConnectionLimit, policy.MaxConnectionLimit);
            Assert.Equal(DefaultMaxRetryAttempts, policy.RetryOptions.MaxRetryAttemptsOnThrottledRequests);
            Assert.Equal(DefaultMaxRetryWaitTime, policy.RetryOptions.MaxRetryWaitTimeInSeconds);
            Assert.False(policy.EnableEndpointDiscovery);
            Assert.Empty(policy.PreferredLocations);
        }

        [Fact]
        public void ParseCosmosDBClientSettings_Should_Work_With_Custom_Config()
        {
            var config = new NameValueCollection();
            config["connectionMode"] = "Gateway";
            config["connectionProtocol"] = "Https";
            config["requestTimeout"] = "1";
            config["maxConnectionLimit"] = "10";
            config["maxRetryAttemptsOnThrottledRequests"] = "11";
            config["maxRetryWaitTimeInSeconds"] = "5";
            config["preferredLocations"] = "West US;Japan West";

            CosmosDBSessionStateProviderAsync.ResetStaticFields();
            var policy = CosmosDBSessionStateProviderAsync.ParseCosmosDBClientSettings(config);

            Assert.Equal(ConnectionMode.Gateway, policy.ConnectionMode);
            Assert.Equal(Protocol.Https, policy.ConnectionProtocol);
            Assert.Equal(TimeSpan.FromSeconds(1), policy.RequestTimeout);
            Assert.Equal(10, policy.MaxConnectionLimit);
            Assert.Equal(11, policy.RetryOptions.MaxRetryAttemptsOnThrottledRequests);
            Assert.Equal(5, policy.RetryOptions.MaxRetryWaitTimeInSeconds);
            Assert.True(policy.EnableEndpointDiscovery);
            Assert.Collection(policy.PreferredLocations, loc => Assert.Equal("West US", loc), loc => Assert.Equal("Japan West", loc));
        }

        [Fact]
        public async void ExecuteStoredProcedureWithWrapperAsync_Should_Execute_DocumentDBSP()
        {
            CosmosDBSessionStateProviderAsync.ResetStaticFields();
            var docClientMoq = new Mock<IDocumentClient>();
            var result = new StoredProcedureResponse<object>();

            docClientMoq.Setup(client => client.ExecuteStoredProcedureAsync<object>(It.IsAny<Uri>(), It.IsAny<RequestOptions>(), It.IsAny<object[]>()))
            .Returns(Task.FromResult(result));

            CosmosDBSessionStateProviderAsync.Client = docClientMoq.Object;

            var resp = await CosmosDBSessionStateProviderAsync.ExecuteStoredProcedureWithWrapperAsync<object>(TestSPLink, new RequestOptions(), 1);

            Assert.Same(result, resp);
        }

        [Fact]
        public async void ExecuteStoredProcedureWithWrapperAsync_Should_Handle_Rejection_DueTo_LargeRequests_DocumentClientException()
        {
            CosmosDBSessionStateProviderAsync.ResetStaticFields();
            var docClientMoq = new Mock<IDocumentClient>();
            var result = new StoredProcedureResponse<object>();

            var docException = CreateDocumentClientException((HttpStatusCode)429);
            docClientMoq.Setup(client => client.ExecuteStoredProcedureAsync<object>(It.IsAny<Uri>(), It.IsAny<RequestOptions>(), It.IsAny<object[]>()))
            .Throws(docException);

            CosmosDBSessionStateProviderAsync.Client = docClientMoq.Object;

            var ex = await Assert.ThrowsAsync<Exception>(async () => await CosmosDBSessionStateProviderAsync.ExecuteStoredProcedureWithWrapperAsync<object>(TestSPLink, new RequestOptions(), 1));
            Assert.Equal(RequestTooLargeExceptionMsg, ex.Message);
        }

        [Fact]
        public async void ExecuteStoredProcedureWithWrapperAsync_Should_Handle_Rejection_DueTo_LargeRequests_AggregateException()
        {
            CosmosDBSessionStateProviderAsync.ResetStaticFields();
            var docClientMoq = new Mock<IDocumentClient>();
            var result = new StoredProcedureResponse<object>();

            var docException = CreateDocumentClientException((HttpStatusCode)429);
            var aggException = new AggregateException(docException);
            docClientMoq.Setup(client => client.ExecuteStoredProcedureAsync<object>(It.IsAny<Uri>(), It.IsAny<RequestOptions>(), It.IsAny<object[]>()))
            .Throws(aggException);

            CosmosDBSessionStateProviderAsync.Client = docClientMoq.Object;

            var ex = await Assert.ThrowsAsync<Exception>(async () => await CosmosDBSessionStateProviderAsync.ExecuteStoredProcedureWithWrapperAsync<object>(TestSPLink, new RequestOptions(), 1));
            Assert.Equal(RequestTooLargeExceptionMsg, ex.Message);
        }

        [Fact]
        public async void CreateSessionStateItemAsync_Should_Execute_CreateSessionStateItemSP_If_Not_Using_Partition()
        {
            var docClientMoq = new Mock<IDocumentClient>();
            var spResponse = CreateStoredProcedureResponseInstance<object>(HttpStatusCode.OK, null);
            var spLink = UriFactory.CreateStoredProcedureUri(DatabaseId, CollectionId, CreateSessionStateItemSPID);
            object[] ssData = null;

            SetupReadDocumentCollectionAsyncMock(docClientMoq, UriFactory.CreateDocumentCollectionUri(DatabaseId, CollectionId), () => { });
            docClientMoq.Setup(client => client.ExecuteStoredProcedureAsync<object>(
                It.Is<Uri>(link => link.ToString() == spLink.ToString()), 
                It.Is<RequestOptions>(option => option.PartitionKey == null),
                It.Is<object[]>(parameters => parameters.Count() == 5)))
            .Returns(Task.FromResult(spResponse))
            .Callback<Uri, RequestOptions, object[]>((_, __, parameters) => ssData = parameters);

            var provider = CreateAndInitializeProviderWithDefaultConfig((_, __, ___) => docClientMoq.Object);
            var buff = Convert.ToBase64String(new byte[DefaultItemLength]);
            var exception = await Record.ExceptionAsync(
                async() => await provider.CreateSessionStateItemAsync(TestSessionId, DefaultSessionTimeoutInSec, buff, true));

            Assert.Null(exception);
            Assert.NotNull(ssData);
            // sessionId, timeout, lockCookie, sessionItem, uninitialized
            Assert.Equal(TestSessionId, (string)ssData[0]);
            Assert.Equal(DefaultSessionTimeoutInSec, (int)ssData[1]);
            Assert.Equal(DefaultLockCookie, (int)ssData[2]);
            Assert.Equal(buff, (string)ssData[3]);
            Assert.True((bool)ssData[4]);
        }

        [Fact]
        public async void CreateSessionStateItemAsync_Should_Execute_CreateSessionStateItemInPartitionSP_If_Using_Partition()
        {
            var docClientMoq = new Mock<IDocumentClient>();
            var spResponse = CreateStoredProcedureResponseInstance<object>(HttpStatusCode.OK, null);
            var spLink = UriFactory.CreateStoredProcedureUri(DatabaseId, CollectionId, CreateSessionStateItemInPartitionSPID);
            object[] ssData = null;

            SetupReadDocumentCollectionAsyncMock(docClientMoq, UriFactory.CreateDocumentCollectionUri(DatabaseId, CollectionId), () => { });
            docClientMoq.Setup(client => client.ExecuteStoredProcedureAsync<object>(
                It.Is<Uri>(link => link.ToString() == spLink.ToString()),
                It.Is<RequestOptions>(option => option.PartitionKey != null),
                It.Is<object[]>(parameters => parameters.Count() == 6)))
            .Returns(Task.FromResult(spResponse))
            .Callback<Uri, RequestOptions, object[]>((_, __, parameters) => ssData = parameters);

            var provider = CreateAndInitializeProviderWithDefaultConfig((_, __, ___) => docClientMoq.Object, true);
            var buff = Convert.ToBase64String(new byte[DefaultItemLength]);
            var exception = await Record.ExceptionAsync(
                async () => await provider.CreateSessionStateItemAsync(TestSessionId, DefaultSessionTimeoutInSec, buff, true));

            Assert.Null(exception);
            Assert.NotNull(ssData);
            // sessionId, partitionValue, timeout, lockCookie, sessionItem, uninitialized
            Assert.Equal(TestSessionId, (string)ssData[0]);
            Assert.Equal(DefaultPartitionValue, (string)ssData[1]);
            Assert.Equal(DefaultSessionTimeoutInSec, (int)ssData[2]);
            Assert.Equal(DefaultLockCookie, (int)ssData[3]);
            Assert.Equal(buff, (string)ssData[4]);
            Assert.True((bool)ssData[5]);
        }

        [Fact]
        public async void CreateSessionStateItemAsync_Should_Execute_CreateSessionStateItemInPartitionSP_With_Full_SessionID_If_Using_Wildcard()
        {
            var docClientMoq = new Mock<IDocumentClient>();
            var spResponse = CreateStoredProcedureResponseInstance<object>(HttpStatusCode.OK, null);
            var spLink = UriFactory.CreateStoredProcedureUri(DatabaseId, CollectionId, CreateSessionStateItemInPartitionSPID);
            object[] ssData = null;

            SetupReadDocumentCollectionAsyncMock(docClientMoq, UriFactory.CreateDocumentCollectionUri(DatabaseId, CollectionId), () => { });
            docClientMoq.Setup(client => client.ExecuteStoredProcedureAsync<object>(
                It.Is<Uri>(link => link.ToString() == spLink.ToString()),
                It.Is<RequestOptions>(option => option.PartitionKey != null),
                It.Is<object[]>(parameters => parameters.Count() == 6)))
            .Returns(Task.FromResult(spResponse))
            .Callback<Uri, RequestOptions, object[]>((_, __, parameters) => ssData = parameters);

            var provider = CreateAndInitializeProviderWithDefaultConfig((_, __, ___) => docClientMoq.Object, true, false, WildcardPartitionString);
            var buff = Convert.ToBase64String(new byte[DefaultItemLength]);
            var exception = await Record.ExceptionAsync(
                async () => await provider.CreateSessionStateItemAsync(TestSessionId, DefaultSessionTimeoutInSec, buff, true));

            Assert.Null(exception);
            Assert.NotNull(ssData);
            // sessionId, partitionValue, timeout, lockCookie, sessionItem, uninitialized
            Assert.Equal(TestSessionId, (string)ssData[0]);
            Assert.Equal(TestSessionId, (string)ssData[1]);
            Assert.Equal(DefaultSessionTimeoutInSec, (int)ssData[2]);
            Assert.Equal(DefaultLockCookie, (int)ssData[3]);
            Assert.Equal(buff, (string)ssData[4]);
            Assert.True((bool)ssData[5]);
        }

        [Fact]
        public async void CreateUninitializedItemAsync_Should_Create_SessionItem_In_DocumentDB()
        {
            var docClientMoq = new Mock<IDocumentClient>();
            var spResponse = CreateStoredProcedureResponseInstance<object>(HttpStatusCode.OK, null);
            var spLink = UriFactory.CreateStoredProcedureUri(DatabaseId, CollectionId, CreateSessionStateItemSPID);
            object[] ssData = null;

            SetupReadDocumentCollectionAsyncMock(docClientMoq, UriFactory.CreateDocumentCollectionUri(DatabaseId, CollectionId), () => { });
            docClientMoq.Setup(client => client.ExecuteStoredProcedureAsync<object>(
                It.Is<Uri>(link => link.ToString() == spLink.ToString()),
                It.Is<RequestOptions>(option => option.PartitionKey == null),
                It.Is<object[]>(parameters => parameters.Count() == 5)))
            .Returns(Task.FromResult(spResponse))
            .Callback<Uri, RequestOptions, object[]>((_, __, parameters) => ssData = parameters);

            var provider = CreateAndInitializeProviderWithDefaultConfig((_, __, ___) => docClientMoq.Object);
            var httpContext = CreateMoqHttpContextBase();
            var exception = await Record.ExceptionAsync(
                async () => await provider.CreateUninitializedItemAsync(httpContext, TestSessionId, 
                                                                        DefaultSessionTimeout, CancellationToken.None));

            Assert.Null(exception);
            Assert.NotNull(ssData);
            // sessionId, timeout, lockCookie, sessionItem, uninitialized
            Assert.Equal(TestSessionId, (string)ssData[0]);
            Assert.Equal(DefaultSessionTimeoutInSec, (int)ssData[1]);
            Assert.Equal(DefaultLockCookie, (int)ssData[2]);
            Assert.True((bool)ssData[4]);
        }

        [Theory]
        [InlineData(SessionStateActions.None, false)]
        [InlineData(SessionStateActions.InitializeItem, true)]
        public async void GetItemAsync_Should_Return_SessionItem_If_SessionItem_Is_Unlocked(SessionStateActions action, bool compressionEnabled)
        {
            var docClientMoq = new Mock<IDocumentClient>();
            var sessionCollection = new SessionStateItemCollection();
            var now = DateTime.UtcNow;
            sessionCollection["test1"] = "test1";
            sessionCollection["test2"] = now;
            var data = new SessionStateStoreData(sessionCollection, new HttpStaticObjectsCollection(), DefaultSessionTimeout);
            string buff;
            CosmosDBSessionStateProviderAsync.SerializeStoreData(data, out buff, compressionEnabled);

            var expectedSSItem = new SessionStateItem()
            {
                SessionId = TestSessionId,
                Actions = action,
                Locked = false,
                SessionItem = Convert.FromBase64String(buff),
                LockAge = TimeSpan.Zero,
                LockCookie = DefaultLockCookie,
                Timeout = DefaultSessionTimeoutInSec
            };

            var spResponse = CreateStoredProcedureResponseInstance<SessionStateItem>(HttpStatusCode.OK, expectedSSItem);
            var spLink = UriFactory.CreateStoredProcedureUri(DatabaseId, CollectionId, GetStateItemSPID);

            SetupReadDocumentCollectionAsyncMock(docClientMoq, UriFactory.CreateDocumentCollectionUri(DatabaseId, CollectionId), () => { });
            docClientMoq.Setup(client => client.ExecuteStoredProcedureAsync<SessionStateItem>(
                It.Is<Uri>(link => link.ToString() == spLink.ToString()),
                It.Is<RequestOptions>(option => option.PartitionKey == null),
                It.Is<string>(sessionid => sessionid.Contains(TestSessionId))))
            .Returns(Task.FromResult(spResponse));

            var provider = CreateAndInitializeProviderWithDefaultConfig((_, __, ___) => docClientMoq.Object, false, compressionEnabled);
            var httpContext = CreateMoqHttpContextBase();
            var itemResult = await provider.GetItemAsync(httpContext, TestSessionId, CancellationToken.None);

            Assert.NotNull(itemResult);
            Assert.Equal(expectedSSItem.Actions, itemResult.Actions);
            Assert.Equal(sessionCollection.Count, itemResult.Item.Items.Count);
            Assert.Equal("test1", itemResult.Item.Items["test1"]);
            Assert.Equal(now, (DateTime)itemResult.Item.Items["test2"]);
            Assert.False(itemResult.Locked);
        }

        [Fact]
        public async void GetItemAsync_Should_Return_SessionItem_With_Null_IF_SessionItem_Is_Locked()
        {
            var docClientMoq = new Mock<IDocumentClient>();

            var expectedSSItem = new SessionStateItem()
            {
                SessionId = TestSessionId,
                Actions = SessionStateActions.None,
                Locked = true,
                SessionItem = null,
                LockAge = TimeSpan.FromSeconds(2),
                LockCookie = DefaultLockCookie,
                Timeout = DefaultSessionTimeoutInSec
            };

            var spResponse = CreateStoredProcedureResponseInstance<SessionStateItem>(HttpStatusCode.OK, expectedSSItem);
            var spLink = UriFactory.CreateStoredProcedureUri(DatabaseId, CollectionId, GetStateItemSPID);

            SetupReadDocumentCollectionAsyncMock(docClientMoq, UriFactory.CreateDocumentCollectionUri(DatabaseId, CollectionId), () => { });
            docClientMoq.Setup(client => client.ExecuteStoredProcedureAsync<SessionStateItem>(
                It.Is<Uri>(link => link.ToString() == spLink.ToString()),
                It.Is<RequestOptions>(option => option.PartitionKey == null),
                It.Is<string>(sessionid => sessionid.Contains(TestSessionId))))
            .Returns(Task.FromResult(spResponse));

            var provider = CreateAndInitializeProviderWithDefaultConfig((_, __, ___) => docClientMoq.Object);
            var httpContext = CreateMoqHttpContextBase();
            var itemResult = await provider.GetItemAsync(httpContext, TestSessionId, CancellationToken.None);

            Assert.NotNull(itemResult);
            Assert.Equal(expectedSSItem.Actions, itemResult.Actions);
            Assert.Null(itemResult.Item);
            Assert.True(itemResult.Locked);
            Assert.Equal(expectedSSItem.Actions, itemResult.Actions);
            Assert.Equal(expectedSSItem.LockCookie, itemResult.LockId);            
        }

        [Fact]
        public async void GetItemExclusiveAsync_Should_Return_SessionItem_If_SessionItem_Is_Unlocked()
        {
            var docClientMoq = new Mock<IDocumentClient>();
            var sessionCollection = new SessionStateItemCollection();
            var now = DateTime.UtcNow;
            sessionCollection["test1"] = "test1";
            sessionCollection["test2"] = now;
            var data = new SessionStateStoreData(sessionCollection, new HttpStaticObjectsCollection(), DefaultSessionTimeout);
            string buff;
            CosmosDBSessionStateProviderAsync.SerializeStoreData(data, out buff, true);

            var expectedSSItem = new SessionStateItem()
            {
                SessionId = TestSessionId,
                Actions =  SessionStateActions.None,
                Locked = false,
                SessionItem = Convert.FromBase64String(buff),
                LockAge = TimeSpan.Zero,
                LockCookie = DefaultLockCookie,
                Timeout = DefaultSessionTimeoutInSec
            };

            var spResponse = CreateStoredProcedureResponseInstance<SessionStateItem>(HttpStatusCode.OK, expectedSSItem);
            var spLink = UriFactory.CreateStoredProcedureUri(DatabaseId, CollectionId, GetStateItemExclusiveSPID);

            SetupReadDocumentCollectionAsyncMock(docClientMoq, UriFactory.CreateDocumentCollectionUri(DatabaseId, CollectionId), () => { });
            docClientMoq.Setup(client => client.ExecuteStoredProcedureAsync<SessionStateItem>(
                It.Is<Uri>(link => link.ToString() == spLink.ToString()),
                It.Is<RequestOptions>(option => option.PartitionKey != null),
                It.Is<string>(sessionid => sessionid == TestSessionId)))
            .Returns(Task.FromResult(spResponse));

            var provider = CreateAndInitializeProviderWithDefaultConfig((_, __, ___) => docClientMoq.Object, true, true);
            var httpContext = CreateMoqHttpContextBase();
            var itemResult = await provider.GetItemExclusiveAsync(httpContext, TestSessionId, CancellationToken.None);

            Assert.NotNull(itemResult);
            Assert.Equal(sessionCollection.Count, itemResult.Item.Items.Count);
            Assert.Equal("test1", itemResult.Item.Items["test1"]);
            Assert.Equal(now, (DateTime)itemResult.Item.Items["test2"]);
            Assert.False(itemResult.Locked);
            Assert.Equal(expectedSSItem.LockAge, itemResult.LockAge);
            Assert.Equal(expectedSSItem.LockCookie, itemResult.LockId);
            Assert.Equal(expectedSSItem.Actions, itemResult.Actions);
        }

        [Fact]
        public async void GetItemExclusiveAsync_Should_Return_SessionItem_With_Null_IF_SessionItem_Is_Locked()
        {
            var docClientMoq = new Mock<IDocumentClient>();
            var expectedSSItem = new SessionStateItem()
            {
                SessionId = TestSessionId,
                Actions = SessionStateActions.None,
                Locked = true,
                SessionItem = null,
                LockAge = TimeSpan.FromSeconds(2),
                LockCookie = DefaultLockCookie,
                Timeout = DefaultSessionTimeoutInSec
            };

            var spResponse = CreateStoredProcedureResponseInstance<SessionStateItem>(HttpStatusCode.OK, expectedSSItem);
            var spLink = UriFactory.CreateStoredProcedureUri(DatabaseId, CollectionId, GetStateItemExclusiveSPID);

            SetupReadDocumentCollectionAsyncMock(docClientMoq, UriFactory.CreateDocumentCollectionUri(DatabaseId, CollectionId), () => { });
            docClientMoq.Setup(client => client.ExecuteStoredProcedureAsync<SessionStateItem>(
                It.Is<Uri>(link => link.ToString() == spLink.ToString()),
                It.Is<RequestOptions>(option => option.PartitionKey == null),
                It.Is<string>(sessionid => sessionid == TestSessionId)))
            .Returns(Task.FromResult(spResponse));

            var provider = CreateAndInitializeProviderWithDefaultConfig((_, __, ___) => docClientMoq.Object);
            var httpContext = CreateMoqHttpContextBase();
            var itemResult = await provider.GetItemExclusiveAsync(httpContext, TestSessionId, CancellationToken.None);

            Assert.NotNull(itemResult);
            Assert.Equal(expectedSSItem.Actions, itemResult.Actions);
            Assert.True(itemResult.Locked);
            Assert.Equal(expectedSSItem.LockAge, itemResult.LockAge);
            Assert.Equal(expectedSSItem.LockCookie, itemResult.LockId);
            Assert.Null(itemResult.Item);
        }

        [Fact]
        public async void ReleaseItemExclusiveAsync_Should_Release_SessionItem()
        {
            var docClientMoq = new Mock<IDocumentClient>();
            var spResponse = CreateStoredProcedureResponseInstance<object>(HttpStatusCode.OK, null);
            var spLink = UriFactory.CreateStoredProcedureUri(DatabaseId, CollectionId, ReleaseItemExclusiveSPID);
            var lockId = 10;

            SetupReadDocumentCollectionAsyncMock(docClientMoq, UriFactory.CreateDocumentCollectionUri(DatabaseId, CollectionId), () => { });
            docClientMoq.Setup(client => client.ExecuteStoredProcedureAsync<object>(
                It.Is<Uri>(link => link.ToString() == spLink.ToString()),
                It.Is<RequestOptions>(option => option.PartitionKey == null),
                It.Is<string>(id => TestSessionId == id),
                It.Is<int>(lockCookie => lockCookie == lockId)))
            .Returns(Task.FromResult(spResponse));

            var provider = CreateAndInitializeProviderWithDefaultConfig((_, __, ___) => docClientMoq.Object);
            var httpContext = CreateMoqHttpContextBase();
            
            var exception = await Record.ExceptionAsync(
                async () => await provider.ReleaseItemExclusiveAsync(httpContext, TestSessionId, lockId, CancellationToken.None));

            Assert.Null(exception);
        }

        [Fact]
        public async void RemoveItemAsync_Should_Remove_SessionItem()
        {
            var docClientMoq = new Mock<IDocumentClient>();
            var spResponse = CreateStoredProcedureResponseInstance<object>(HttpStatusCode.OK, null);
            var spLink = UriFactory.CreateStoredProcedureUri(DatabaseId, CollectionId, RemoveStateItemSPID);
            var lockId = 10;

            SetupReadDocumentCollectionAsyncMock(docClientMoq, UriFactory.CreateDocumentCollectionUri(DatabaseId, CollectionId), () => { });
            docClientMoq.Setup(client => client.ExecuteStoredProcedureAsync<object>(
                It.Is<Uri>(link => link.ToString() == spLink.ToString()),
                It.Is<RequestOptions>(option => option.PartitionKey == null),
                It.Is<string>(id => TestSessionId == id),
                It.Is<int>(lockCookie => lockCookie == lockId)))
            .Returns(Task.FromResult(spResponse));

            var provider = CreateAndInitializeProviderWithDefaultConfig((_, __, ___) => docClientMoq.Object);
            var httpContext = CreateMoqHttpContextBase();

            var exception = await Record.ExceptionAsync(
                async () => await provider.RemoveItemAsync(httpContext, TestSessionId, lockId, null, CancellationToken.None));

            Assert.Null(exception);
        }

        [Fact]
        public async void ResetItemTimeoutAsync_Should_Reset_SessionItem_Timeout()
        {
            var docClientMoq = new Mock<IDocumentClient>();
            var spResponse = CreateStoredProcedureResponseInstance<object>(HttpStatusCode.OK, null);
            var spLink = UriFactory.CreateStoredProcedureUri(DatabaseId, CollectionId, ResetItemTimeoutSPID);

            SetupReadDocumentCollectionAsyncMock(docClientMoq, UriFactory.CreateDocumentCollectionUri(DatabaseId, CollectionId), () => { });
            docClientMoq.Setup(client => client.ExecuteStoredProcedureAsync<object>(
                It.Is<Uri>(link => link.ToString() == spLink.ToString()),
                It.Is<RequestOptions>(option => option.PartitionKey == null),
                It.Is<string>(id => TestSessionId == id)))
            .Returns(Task.FromResult(spResponse));

            var provider = CreateAndInitializeProviderWithDefaultConfig((_, __, ___) => docClientMoq.Object);
            var httpContext = CreateMoqHttpContextBase();

            var exception = await Record.ExceptionAsync(
                async () => await provider.ResetItemTimeoutAsync(httpContext, TestSessionId, CancellationToken.None));

            Assert.Null(exception);
        }

        [Fact]
        public async void SetAndReleaseItemExclusiveAsync_Should_Create_New_SessionItem_If_Session_Is_New()
        {
            var docClientMoq = new Mock<IDocumentClient>();
            var spResponse = CreateStoredProcedureResponseInstance<object>(HttpStatusCode.OK, null);
            var spLink = UriFactory.CreateStoredProcedureUri(DatabaseId, CollectionId, CreateSessionStateItemSPID);
            var sessionCollection = new SessionStateItemCollection();
            var now = DateTime.UtcNow;
            sessionCollection["test1"] = "test1";
            sessionCollection["test2"] = now;
            var data = new SessionStateStoreData(sessionCollection, new HttpStaticObjectsCollection(), DefaultSessionTimeout);
            object[] ssData = null;

            SetupReadDocumentCollectionAsyncMock(docClientMoq, UriFactory.CreateDocumentCollectionUri(DatabaseId, CollectionId), () => { });
            docClientMoq.Setup(client => client.ExecuteStoredProcedureAsync<object>(
                It.Is<Uri>(link => link.ToString() == spLink.ToString()),
                It.Is<RequestOptions>(option => option.PartitionKey == null),
                It.Is<object[]>(parameters => parameters.Count() == 5)))
            .Returns(Task.FromResult(spResponse))
            .Callback<Uri, RequestOptions, object[]>((_, __, parameters) => ssData = parameters);

            var provider = CreateAndInitializeProviderWithDefaultConfig((_, __, ___) => docClientMoq.Object);
            var httpContext = CreateMoqHttpContextBase();
            var exception = await Record.ExceptionAsync(
                async () => await provider.SetAndReleaseItemExclusiveAsync(httpContext, TestSessionId, data, null, true, CancellationToken.None));

            Assert.Null(exception);
            Assert.NotNull(ssData);
            // sessionId, timeout, lockCookie, sessionItem, uninitialized
            Assert.Equal(TestSessionId, (string)ssData[0]);
            Assert.Equal(DefaultSessionTimeoutInSec, (int)ssData[1]);
            Assert.Equal(DefaultLockCookie, (int)ssData[2]);
            Assert.NotNull((string)ssData[3]);
            Assert.False((bool)ssData[4]);
        }

        [Theory]
        [InlineData(null)]
        [InlineData(AnotherLockCookie)]
        public async void SetAndReleaseItemExclusiveAsync_Should_Release_NonExclsive_SessionItem(object lockcookie)
        {
            var docClientMoq = new Mock<IDocumentClient>();
            var spResponse = CreateStoredProcedureResponseInstance<object>(HttpStatusCode.OK, null);
            var spLink = UriFactory.CreateStoredProcedureUri(DatabaseId, CollectionId, UpdateSessionStateItemSPID);
            var sessionCollection = new SessionStateItemCollection();
            var now = DateTime.UtcNow;
            sessionCollection["test1"] = "test1";
            sessionCollection["test2"] = now;
            var data = new SessionStateStoreData(sessionCollection, new HttpStaticObjectsCollection(), DefaultSessionTimeout);
            object[] ssData = null;

            SetupReadDocumentCollectionAsyncMock(docClientMoq, UriFactory.CreateDocumentCollectionUri(DatabaseId, CollectionId), () => { });
            docClientMoq.Setup(client => client.ExecuteStoredProcedureAsync<object>(
                It.Is<Uri>(link => link.ToString() == spLink.ToString()),
                It.Is<RequestOptions>(option => option.PartitionKey == null),
                It.Is<object[]>(parameters => parameters.Count() == 4)))
            .Returns(Task.FromResult(spResponse))
            .Callback<Uri, RequestOptions, object[]>((_, __, parameters) => ssData = parameters);

            var provider = CreateAndInitializeProviderWithDefaultConfig((_, __, ___) => docClientMoq.Object);
            var httpContext = CreateMoqHttpContextBase();
            var exception = await Record.ExceptionAsync(
                async () => await provider.SetAndReleaseItemExclusiveAsync(httpContext, TestSessionId, data, lockcookie, false, CancellationToken.None));

            Assert.Null(exception);
            Assert.NotNull(ssData);
            //sessionId, lockCookie, timeout, sessionItem
            Assert.Equal(TestSessionId, (string)ssData[0]);
            Assert.Equal(lockcookie ?? DefaultLockCookie, ssData[1]);
            Assert.Equal(DefaultSessionTimeoutInSec, (int)ssData[2]);
            Assert.NotNull((string)ssData[3]);
        }

        private HttpContextBase CreateMoqHttpContextBase()
        {
            var httpContextBaseMoq = new Mock<HttpContextBase>();
            var httpAppMoq = new Mock<HttpApplication>();

            httpContextBaseMoq.SetupGet(cxtBase => cxtBase.ApplicationInstance).Returns(httpAppMoq.Object);

            return httpContextBaseMoq.Object;
        }

        private CosmosDBSessionStateProviderAsync CreateProvider()
        {
            CosmosDBSessionStateProviderAsync.ResetStaticFields();
            var provider = new CosmosDBSessionStateProviderAsync();
            provider.AppId = AppId;
            
            return provider;
        }

        private CosmosDBSessionStateProviderAsync CreateAndInitializeProviderWithDefaultConfig(
            Func<Uri, string, ConnectionPolicy, IDocumentClient> clientFactory, bool enablePartition = false, bool compressionEnabled = false, string partitionNum = null)
        {
            CosmosDBSessionStateProviderAsync.ResetStaticFields();
            var provider = new CosmosDBSessionStateProviderAsync();            
            provider.AppId = AppId;
            provider.DocumentClientFactory = clientFactory;

            var providerConfig = new NameValueCollection();
            var appSettings = new NameValueCollection();
            var ssc = new SessionStateSection();

            if (compressionEnabled)
            {
                ssc.CompressionEnabled = true;
            }

            providerConfig["cosmosDBEndPointSettingKey"] = DefaultEndPointSettingKey;
            appSettings[DefaultEndPointSettingKey] = EndPoint;
            providerConfig["cosmosDBAuthKeySettingKey"] = DefaultAuthKeySettingKey;
            appSettings[DefaultAuthKeySettingKey] = AuthKey;
            providerConfig["databaseId"] = DatabaseId;
            providerConfig["collectionId"] = CollectionId;
            if (enablePartition)
            {
                providerConfig["partitionKeyPath"] = ParitionKeyPath;
                providerConfig["partitionNumUsedByProvider"] = partitionNum ?? PartitionNum.ToString();
            }

            provider.Initialize(DefaultProviderName, providerConfig, ssc, appSettings);

            return provider;
        }

        private DocumentClientException CreateDocumentClientException(HttpStatusCode statusCode = HttpStatusCode.NotFound)
        {
            var cctr = typeof(DocumentClientException).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                new Type[] { typeof(string), typeof(Exception), typeof(HttpResponseHeaders), typeof(HttpStatusCode), typeof(Uri) }, null);

            return (DocumentClientException)cctr.Invoke(new object[] { "", null, null, statusCode, null });
        }

        private void SetupReadSPSucessMock(Mock<IDocumentClient> docClientMoq, string dbId, string collectionId, string spId, Action callback)
        {
            var sPLink = UriFactory.CreateStoredProcedureUri(dbId, collectionId, spId);
            docClientMoq.Setup(client => client.ReadStoredProcedureAsync(sPLink, null))
                .Returns(Task.FromResult((ResourceResponse<StoredProcedure>)null))
                .Callback(callback);
        }

        private void SetupReadSPFailureMock(Mock<IDocumentClient> docClientMoq, string dbId, string collectionId, string spId, DocumentClientException ex)
        {
            var sPLink = UriFactory.CreateStoredProcedureUri(dbId, collectionId, spId);
            docClientMoq.Setup(client => client.ReadStoredProcedureAsync(sPLink, null)).Throws(ex);
        }

        private void SetupCreateSPMock(Mock<IDocumentClient> docClientMoq, Uri docCollectionUri, string spId, Action callback)
        {
            docClientMoq.Setup(client => client.CreateStoredProcedureAsync(It.Is<Uri>(docUri => docCollectionUri.ToString() == docUri.ToString()),
                    It.Is<StoredProcedure>(sp => sp.Id == spId), null))
                .Returns(Task.FromResult((ResourceResponse<StoredProcedure>)null))
                .Callback(callback);
        }

        private void SetupReadDocumentCollectionAsyncMock(Mock<IDocumentClient> docClientMoq, Uri docCollectionUri, Action callback)
        {
            ResourceResponse<DocumentCollection> rr = new ResourceResponse<DocumentCollection>(new DocumentCollection());
            docClientMoq.Setup(client => client.ReadDocumentCollectionAsync(docCollectionUri, null))
                .Returns(Task.FromResult(rr))
                .Callback(callback);

        }

        // Hacky way to create StoredProcedureResponse instance
        // Some methods in IDocumentClient return StoredProcedureResponse which can't be mocked and setup
        private StoredProcedureResponse<T> CreateStoredProcedureResponseInstance<T>(HttpStatusCode statusCode, T responseBody)
        {
            var assembly = typeof(IDocumentClient).Assembly;
            var docServiceResponseType = assembly.GetType("Microsoft.Azure.Documents.DocumentServiceResponse");
            var ctorParamTypes = new Type[] { typeof(Stream), typeof(NameValueCollection), typeof(HttpStatusCode), typeof(JsonSerializerSettings) };
            var ctor = docServiceResponseType.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, ctorParamTypes, null);
            var docServiceResponse = ctor.Invoke(new object[] { null, null, statusCode, null });
            
            var spResponse = new StoredProcedureResponse<T>();            
            var responseField = typeof(StoredProcedureResponse<T>).GetField("response", BindingFlags.NonPublic | BindingFlags.Instance);
            responseField.SetValue(spResponse, docServiceResponse);
            var responseBodyField = typeof(StoredProcedureResponse<T>).GetField("responseBody", BindingFlags.NonPublic | BindingFlags.Instance);
            responseBodyField.SetValue(spResponse, responseBody);

            return spResponse;
        }
    }
}
