// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See the License.txt file in the project root for full license information.

namespace Microsoft.AspNet.SessionState.CosmosDBSessionStateAsyncProvider.Test
{
    using Microsoft.Azure.Cosmos;
    using Microsoft.Azure.Cosmos.Scripts;
    using Moq;
    using System;
    using System.Collections.Specialized;
    using System.IO;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web;
    using System.Web.Configuration;
    using System.Web.SessionState;

    using Xunit;

    public class CosmosDBSessionStateProviderAsyncTest
    {
        private const string CreateSessionStateItemSPID = "CreateSessionStateItem2";
        private const string GetStateItemSPID = "GetStateItem2";
        private const string GetStateItemExclusiveSPID = "GetStateItemExclusive";
        private const string ReleaseItemExclusiveSPID = "ReleaseItemExclusive";
        private const string RemoveStateItemSPID = "RemoveStateItem2";
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
        private const string ContainerId = "TestContainer";
        private const string EndPoint = "https://test.documents.azure.com";
        private const string AuthKey = "[AuthKey]";
        private const string TestSessionId = "piqhlifa30ooedcp1k42mtef";
        private const int DefaultLockCookie = 1;
        private const int AnotherLockCookie = 2;
        private const int DefaultThroughPut = 5000;
        private const string DefaultPartitionKeyPath = "/id";
        private const int DefaultSessionTimeout = 20;
        private const int DefaultSessionTimeoutInSec = 60 * 20;
        private const int AnotherSessionTimeout = 60 * 5;
        private const ConnectionMode DefaultConnectionMode = ConnectionMode.Direct;
        private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(5);
        private const int DefaultMaxConnectionLimit = 50;
        private const int DefaultMaxRetryAttempts = 9;
        private const int DefaultMaxRetryWaitTime = 10;
        private const int DefaultItemLength = 7000;
        private static readonly string TestSP = "test.doc.sp";
        private static string NonMatchingPartitionKey = $"The specified container '{ContainerId}' already exists with a partition key path other than '{DefaultPartitionKeyPath}'.";

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
            providerConfig["containerId"] = ContainerId;

            var docClientMoq = new Mock<CosmosClient>();
            var notFoundClientException = CreateDocumentClientException();

            var createSessionStateItemSPCreated = false;
            var getStateItemExclusiveSPCreated = false;
            var releaseItemExclusiveSPCreated = false;
            var removeStateItemSPCreated = false;
            var resetItemTimeoutSPCreated = false;
            var updateSessionStateItemSPCreated = false;

            docClientMoq.Setup(client => client.CreateDatabaseIfNotExistsAsync(DatabaseId, (int?)null, null, default))
                        .Returns(Task.FromResult((DatabaseResponse)null));

            var containerResponseMoq = new Mock<ContainerResponse>();
            containerResponseMoq.SetupGet(cr => cr.Resource).Returns((ContainerProperties)null);

            var databaseMoq = new Mock<Database>();
            SetupDatabaseMock(databaseMoq, "UnrelatedDatabase");

            docClientMoq.Setup(client => client.GetDatabase(DatabaseId))
                        .Returns(databaseMoq.Object);

            var containerMoq = new Mock<Container>();

            SetupReadSPFailureMock(containerMoq, CreateSessionStateItemSPID, notFoundClientException);
            SetupCreateSPMock(containerMoq, CreateSessionStateItemSPID, () => createSessionStateItemSPCreated = true);

            SetupReadSPFailureMock(containerMoq, GetStateItemExclusiveSPID, notFoundClientException);
            SetupCreateSPMock(containerMoq, GetStateItemExclusiveSPID, () => getStateItemExclusiveSPCreated = true);

            SetupReadSPFailureMock(containerMoq, ReleaseItemExclusiveSPID, notFoundClientException);
            SetupCreateSPMock(containerMoq, ReleaseItemExclusiveSPID, () => releaseItemExclusiveSPCreated = true);

            SetupReadSPFailureMock(containerMoq, RemoveStateItemSPID, notFoundClientException);
            SetupCreateSPMock(containerMoq, RemoveStateItemSPID, () => removeStateItemSPCreated = true);

            SetupReadSPFailureMock(containerMoq, ResetItemTimeoutSPID, notFoundClientException);
            SetupCreateSPMock(containerMoq, ResetItemTimeoutSPID, () => resetItemTimeoutSPCreated = true);

            SetupReadSPFailureMock(containerMoq, UpdateSessionStateItemSPID, notFoundClientException);
            SetupCreateSPMock(containerMoq, UpdateSessionStateItemSPID, () => updateSessionStateItemSPCreated = true);

            docClientMoq.Setup(client => client.GetContainer(DatabaseId, ContainerId))
                        .Returns(containerMoq.Object);

            provider.CosmosClientFactory = (endpoint, authKey, policy) => docClientMoq.Object;

            provider.Initialize(DefaultProviderName, providerConfig, ssc, appSettings);

            Assert.Equal(DefaultSessionTimeoutInSec, provider.Timeout);
            Assert.False(provider.CompressionEnabled);
            Assert.True(createSessionStateItemSPCreated);
            Assert.True(getStateItemExclusiveSPCreated);
            Assert.True(releaseItemExclusiveSPCreated);
            Assert.True(removeStateItemSPCreated);
            Assert.True(resetItemTimeoutSPCreated);
            Assert.True(updateSessionStateItemSPCreated);
        }

        [Fact]
        public void Initialize_With_Existing_CosmosDBCollection_Should_Reuse_It_If_Partitioned_Correctly()
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
            providerConfig["collectionId"] = ContainerId;   // Note this uses the old parameter name. This should work just the same.
            providerConfig["connectionMode"] = "Gateway";
            providerConfig["connectionProtocol"] = "Https";
            providerConfig["requestTimeout"] = "1";
            providerConfig["maxConnectionLimit"] = "10";
            providerConfig["maxRetryAttemptsOnThrottledRequests"] = "11";
            providerConfig["maxRetryWaitTimeInSeconds"] = "5";
            providerConfig["preferredLocations"] = "West US;Japan West";

            var docClientMoq = new Mock<CosmosClient>();

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

            docClientMoq.Setup(client => client.CreateDatabaseIfNotExistsAsync(DatabaseId, (int?)null, null, default))
                        .Returns(Task.FromResult((DatabaseResponse)null));

            var databaseMoq = new Mock<Database>();
            SetupDatabaseMock(databaseMoq);

            docClientMoq.Setup(client => client.GetDatabase(DatabaseId))
                        .Returns(databaseMoq.Object);

            var containerMoq = new Mock<Container>();

            SetupReadSPSucessMock(containerMoq, CreateSessionStateItemSPID, () => createSessionStateItemSPRead = true);
            SetupCreateSPMock(containerMoq, CreateSessionStateItemSPID, () => createSessionStateItemSPCreated = true);

            SetupReadSPSucessMock(containerMoq, GetStateItemExclusiveSPID, () => getStateItemExclusiveSPRead = true);
            SetupCreateSPMock(containerMoq, GetStateItemExclusiveSPID, () => getStateItemExclusiveSPCreated = true);

            SetupReadSPSucessMock(containerMoq, ReleaseItemExclusiveSPID, () => releaseItemExclusiveSPRead = true);
            SetupCreateSPMock(containerMoq, ReleaseItemExclusiveSPID, () => releaseItemExclusiveSPCreated = true);

            SetupReadSPSucessMock(containerMoq, RemoveStateItemSPID, () => removeStateItemSPRead = true);
            SetupCreateSPMock(containerMoq, RemoveStateItemSPID, () => removeStateItemSPCreated = true);

            SetupReadSPSucessMock(containerMoq, ResetItemTimeoutSPID, () => resetItemTimeoutSPRead = true);
            SetupCreateSPMock(containerMoq, ResetItemTimeoutSPID, () => resetItemTimeoutSPCreated = true);

            SetupReadSPSucessMock(containerMoq, UpdateSessionStateItemSPID, () => updateSessionStateItemSPRead = true);
            SetupCreateSPMock(containerMoq, UpdateSessionStateItemSPID, () => updateSessionStateItemSPCreated = true);

            docClientMoq.Setup(client => client.GetContainer(DatabaseId, ContainerId))
                        .Returns(containerMoq.Object);

            CosmosClientOptions configuredOptions = null;
            provider.CosmosClientFactory = (endpoint, authKey, options) => { configuredOptions = options; return docClientMoq.Object; };

            provider.Initialize(DefaultProviderName, providerConfig, ssc, appSettings);

            Assert.Equal(EndPoint, CosmosDBSessionStateProviderAsync.EndPoint);
            Assert.Equal(AuthKey, CosmosDBSessionStateProviderAsync.AuthKey);
            Assert.Equal(DatabaseId, CosmosDBSessionStateProviderAsync.DbId);
            Assert.Equal(ContainerId, CosmosDBSessionStateProviderAsync.ContainerId);
            Assert.Equal(DefaultThroughPut, CosmosDBSessionStateProviderAsync.ThroughPut);

            Assert.NotNull(configuredOptions);
            Assert.Equal(ConnectionMode.Gateway, configuredOptions.ConnectionMode);
            Assert.Equal(TimeSpan.FromSeconds(1), configuredOptions.RequestTimeout);
            Assert.Equal(10, configuredOptions.GatewayModeMaxConnectionLimit);
            Assert.Equal(11, configuredOptions.MaxRetryAttemptsOnRateLimitedRequests);
            Assert.Equal(TimeSpan.FromSeconds(5), configuredOptions.MaxRetryWaitTimeOnRateLimitedRequests);
            Assert.True(configuredOptions.EnableTcpConnectionEndpointRediscovery);
            Assert.Collection(configuredOptions.ApplicationPreferredRegions, loc => Assert.Equal("West US", loc), loc => Assert.Equal("Japan West", loc));

            Assert.Equal(AnotherSessionTimeout, provider.Timeout);
            Assert.True(provider.CompressionEnabled);
            Assert.True(createSessionStateItemSPRead);
            Assert.True(getStateItemExclusiveSPRead);
            Assert.True(releaseItemExclusiveSPRead);
            Assert.True(removeStateItemSPRead);
            Assert.True(resetItemTimeoutSPRead);
            Assert.True(updateSessionStateItemSPRead);
            Assert.False(createSessionStateItemSPCreated);
            Assert.False(getStateItemExclusiveSPCreated);
            Assert.False(releaseItemExclusiveSPCreated);
            Assert.False(removeStateItemSPCreated);
            Assert.False(resetItemTimeoutSPCreated);
            Assert.False(updateSessionStateItemSPCreated);
        }

        [Fact]
        public void Initialize_With_Existing_CosmosDBCollection_Should_Fail_If_Partitioned_Incorrectly()
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
            providerConfig["collectionId"] = ContainerId;   // Note this uses the old parameter name. This should work just the same.
            providerConfig["connectionMode"] = "Gateway";
            providerConfig["connectionProtocol"] = "Https";
            providerConfig["requestTimeout"] = "1";
            providerConfig["maxConnectionLimit"] = "10";
            providerConfig["maxRetryAttemptsOnThrottledRequests"] = "11";
            providerConfig["maxRetryWaitTimeInSeconds"] = "5";
            providerConfig["preferredLocations"] = "West US;Japan West";

            var docClientMoq = new Mock<CosmosClient>();

            docClientMoq.Setup(client => client.CreateDatabaseIfNotExistsAsync(DatabaseId, (int?)null, null, default))
                        .Returns(Task.FromResult((DatabaseResponse)null));

            var databaseMoq = new Mock<Database>();
            SetupDatabaseMock(databaseMoq, ContainerId, "/some_other_pkeypath");

            docClientMoq.Setup(client => client.GetDatabase(DatabaseId))
                        .Returns(databaseMoq.Object);

            var containerMoq = new Mock<Container>();

            docClientMoq.Setup(client => client.GetContainer(DatabaseId, ContainerId))
                        .Returns(containerMoq.Object);

            CosmosClientOptions configuredOptions = null;
            provider.CosmosClientFactory = (endpoint, authKey, options) => { configuredOptions = options; return docClientMoq.Object; };

            var ex = Assert.Throws<AggregateException>(() => provider.Initialize(DefaultProviderName, providerConfig, ssc, appSettings));
            Assert.Equal(NonMatchingPartitionKey, ex.InnerException.Message);
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
            providerConfig["containerId"] = ContainerId;

            var docClientMoq = new Mock<CosmosClient>();

            var databaseMoq = new Mock<Database>();
            SetupDatabaseMock(databaseMoq);

            docClientMoq.Setup(client => client.GetDatabase(DatabaseId))
                        .Returns(databaseMoq.Object);

            var containerMoq = new Mock<Container>();

            SetupReadSPSucessMock(containerMoq, CreateSessionStateItemSPID, () => { });
            SetupCreateSPMock(containerMoq, CreateSessionStateItemSPID, () => { });

            SetupReadSPSucessMock(containerMoq, GetStateItemExclusiveSPID, () => { });
            SetupCreateSPMock(containerMoq, GetStateItemExclusiveSPID, () => { });

            SetupReadSPSucessMock(containerMoq, ReleaseItemExclusiveSPID, () => { });
            SetupCreateSPMock(containerMoq, ReleaseItemExclusiveSPID, () => { });

            SetupReadSPSucessMock(containerMoq, RemoveStateItemSPID, () => { });
            SetupCreateSPMock(containerMoq, RemoveStateItemSPID, () => { });

            SetupReadSPSucessMock(containerMoq, ResetItemTimeoutSPID, () => { });
            SetupCreateSPMock(containerMoq, ResetItemTimeoutSPID, () => { });

            SetupReadSPSucessMock(containerMoq, UpdateSessionStateItemSPID, () => { });
            SetupCreateSPMock(containerMoq, UpdateSessionStateItemSPID, () => { });

            docClientMoq.Setup(client => client.GetContainer(DatabaseId, ContainerId))
                        .Returns(containerMoq.Object);

            provider.CosmosClientFactory = (endpoint, authKey, options) => docClientMoq.Object;
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
            providerConfig["containerId"] = ContainerId;

            CosmosDBSessionStateProviderAsync.ResetStaticFields();
            CosmosDBSessionStateProviderAsync.ParseCosmosDbEndPointSettings(providerConfig, appSettings);

            Assert.Equal(EndPoint, CosmosDBSessionStateProviderAsync.EndPoint);
            Assert.Equal(AuthKey, CosmosDBSessionStateProviderAsync.AuthKey);
            Assert.Equal(DatabaseId, CosmosDBSessionStateProviderAsync.DbId);
            Assert.Equal(ContainerId, CosmosDBSessionStateProviderAsync.ContainerId);
            Assert.Equal(DefaultThroughPut, CosmosDBSessionStateProviderAsync.ThroughPut);
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
            providerConfig["containerId"] = ContainerId;

            CosmosDBSessionStateProviderAsync.ResetStaticFields();
            CosmosDBSessionStateProviderAsync.ParseCosmosDbEndPointSettings(providerConfig, appSettings);

            Assert.Equal(EndPoint, CosmosDBSessionStateProviderAsync.EndPoint);
            Assert.Equal(AuthKey, CosmosDBSessionStateProviderAsync.AuthKey);
            Assert.Equal(DatabaseId, CosmosDBSessionStateProviderAsync.DbId);
            Assert.Equal(ContainerId, CosmosDBSessionStateProviderAsync.ContainerId);
            Assert.Equal(DefaultThroughPut, CosmosDBSessionStateProviderAsync.ThroughPut);
        }

        [Fact]
        public void ParseCosmosDbEndPointSettings_Should_Initialize_EndPointSetting_With_Old_Parameters()
        {
            var providerConfig = new NameValueCollection();
            var appSettings = new NameValueCollection();

            providerConfig["cosmosDBEndPointSettingKey"] = DefaultEndPointSettingKey;
            appSettings[DefaultEndPointSettingKey] = EndPoint;
            providerConfig["cosmosDBAuthKeySettingKey"] = DefaultAuthKeySettingKey;
            appSettings[DefaultAuthKeySettingKey] = AuthKey;
            providerConfig["databaseId"] = DatabaseId;
            providerConfig["collectionId"] = ContainerId;   // Note the old parameter name. Should still work the same.
            providerConfig["partitionKeyPath"] = "/some_ignored_path";   // Also note this old parameter. It means nothing, but it should not cause failure.
            providerConfig["partitionNumUsedByProvider"] = "12";   // Same.

            CosmosDBSessionStateProviderAsync.ResetStaticFields();
            CosmosDBSessionStateProviderAsync.ParseCosmosDbEndPointSettings(providerConfig, appSettings);

            Assert.Equal(EndPoint, CosmosDBSessionStateProviderAsync.EndPoint);
            Assert.Equal(AuthKey, CosmosDBSessionStateProviderAsync.AuthKey);
            Assert.Equal(DatabaseId, CosmosDBSessionStateProviderAsync.DbId);
            Assert.Equal(ContainerId, CosmosDBSessionStateProviderAsync.ContainerId);
            Assert.Equal(DefaultThroughPut, CosmosDBSessionStateProviderAsync.ThroughPut);
        }

        [Fact]
        public void ParseCosmosDBClientSettings_Should_Work_Without_Config()
        {
            var config = new NameValueCollection();

            CosmosDBSessionStateProviderAsync.ResetStaticFields();
            var policy = CosmosDBSessionStateProviderAsync.ParseCosmosDBClientSettings(config);

            Assert.Equal(DefaultConnectionMode, policy.ConnectionMode);
            Assert.Equal(DefaultRequestTimeout, policy.RequestTimeout);
            Assert.Equal(DefaultMaxConnectionLimit, policy.GatewayModeMaxConnectionLimit);
            Assert.Equal(DefaultMaxRetryAttempts, policy.MaxRetryAttemptsOnRateLimitedRequests);
            Assert.Equal(TimeSpan.FromSeconds(DefaultMaxRetryWaitTime), policy.MaxRetryWaitTimeOnRateLimitedRequests);
            Assert.False(policy.EnableTcpConnectionEndpointRediscovery);
            Assert.Null(policy.ApplicationPreferredRegions);
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
            Assert.Equal(TimeSpan.FromSeconds(1), policy.RequestTimeout);
            Assert.Equal(10, policy.GatewayModeMaxConnectionLimit);
            Assert.Equal(11, policy.MaxRetryAttemptsOnRateLimitedRequests);
            Assert.Equal(TimeSpan.FromSeconds(5), policy.MaxRetryWaitTimeOnRateLimitedRequests);
            Assert.True(policy.EnableTcpConnectionEndpointRediscovery);
            Assert.Collection(policy.ApplicationPreferredRegions, loc => Assert.Equal("West US", loc), loc => Assert.Equal("Japan West", loc));
        }

        [Fact]
        public async void ExecuteStoredProcedureWithWrapperAsync_Should_Execute_DocumentDBSP()
        {
            var providerConfig = new NameValueCollection();
            var appSettings = new NameValueCollection();

            providerConfig["cosmosDBEndPointSettingKey"] = DefaultEndPointSettingKey;
            appSettings[DefaultEndPointSettingKey] = EndPoint;
            providerConfig["cosmosDBAuthKeySettingKey"] = DefaultAuthKeySettingKey;
            appSettings[DefaultAuthKeySettingKey] = AuthKey;
            providerConfig["databaseId"] = DatabaseId;
            providerConfig["containerId"] = ContainerId;

            CosmosDBSessionStateProviderAsync.ResetStaticFields();
            CosmosDBSessionStateProviderAsync.ParseCosmosDbEndPointSettings(providerConfig, appSettings);

            var docClientMoq = new Mock<CosmosClient>();
            var result = new Mock<StoredProcedureExecuteResponse<object>>();

            var containerMoq = new Mock<Container>();

            containerMoq.Setup(client => client.Scripts.ExecuteStoredProcedureAsync<object>(
                            It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<object[]>(), null, default))
                        .Returns(Task.FromResult(result.Object));

            docClientMoq.Setup(client => client.GetContainer(DatabaseId, ContainerId))
                        .Returns(containerMoq.Object);

            CosmosDBSessionStateProviderAsync.Client = docClientMoq.Object;

            var resp = await CosmosDBSessionStateProviderAsync.ExecuteStoredProcedureWithWrapperAsync<object>(TestSP, TestSessionId, 1);

            Assert.Same(result.Object, resp);
        }

        [Fact]
        public async void ExecuteStoredProcedureWithWrapperAsync_Should_Handle_Rejection_DueTo_LargeRequests_DocumentClientException()
        {
            var providerConfig = new NameValueCollection();
            var appSettings = new NameValueCollection();

            providerConfig["cosmosDBEndPointSettingKey"] = DefaultEndPointSettingKey;
            appSettings[DefaultEndPointSettingKey] = EndPoint;
            providerConfig["cosmosDBAuthKeySettingKey"] = DefaultAuthKeySettingKey;
            appSettings[DefaultAuthKeySettingKey] = AuthKey;
            providerConfig["databaseId"] = DatabaseId;
            providerConfig["containerId"] = ContainerId;

            CosmosDBSessionStateProviderAsync.ResetStaticFields();
            CosmosDBSessionStateProviderAsync.ParseCosmosDbEndPointSettings(providerConfig, appSettings);

            var docClientMoq = new Mock<CosmosClient>();

            var docException = CreateDocumentClientException((HttpStatusCode)429);

            var containerMoq = new Mock<Container>();

            containerMoq.Setup(client => client.Scripts.ExecuteStoredProcedureAsync<object>(
                            It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<object[]>(), null, default))
                        .Throws(docException);

            docClientMoq.Setup(client => client.GetContainer(DatabaseId, ContainerId))
                        .Returns(containerMoq.Object);

            CosmosDBSessionStateProviderAsync.Client = docClientMoq.Object;

            var ex = await Assert.ThrowsAsync<Exception>(async () => await CosmosDBSessionStateProviderAsync.ExecuteStoredProcedureWithWrapperAsync<object>(TestSP, TestSessionId, 1));
            Assert.Equal(RequestTooLargeExceptionMsg, ex.Message);
        }

        [Fact]
        public async void ExecuteStoredProcedureWithWrapperAsync_Should_Handle_Rejection_DueTo_LargeRequests_AggregateException()
        {
            var providerConfig = new NameValueCollection();
            var appSettings = new NameValueCollection();

            providerConfig["cosmosDBEndPointSettingKey"] = DefaultEndPointSettingKey;
            appSettings[DefaultEndPointSettingKey] = EndPoint;
            providerConfig["cosmosDBAuthKeySettingKey"] = DefaultAuthKeySettingKey;
            appSettings[DefaultAuthKeySettingKey] = AuthKey;
            providerConfig["databaseId"] = DatabaseId;
            providerConfig["containerId"] = ContainerId;

            CosmosDBSessionStateProviderAsync.ResetStaticFields();
            CosmosDBSessionStateProviderAsync.ParseCosmosDbEndPointSettings(providerConfig, appSettings);

            var docClientMoq = new Mock<CosmosClient>();

            var docException = CreateDocumentClientException((HttpStatusCode)429);
            var aggException = new AggregateException(docException);

            var containerMoq = new Mock<Container>();

            containerMoq.Setup(container => container.Scripts.ExecuteStoredProcedureAsync<object>(
                            It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<object[]>(), null, default))
                        .Throws(aggException);

            docClientMoq.Setup(client => client.GetContainer(DatabaseId, ContainerId))
                        .Returns(containerMoq.Object);

            CosmosDBSessionStateProviderAsync.Client = docClientMoq.Object;

            var ex = await Assert.ThrowsAsync<Exception>(async () => await CosmosDBSessionStateProviderAsync.ExecuteStoredProcedureWithWrapperAsync<object>(TestSP, TestSessionId, 1));
            Assert.Equal(RequestTooLargeExceptionMsg, ex.Message);
        }

        [Fact]
        public async void CreateUninitializedItemAsync_Should_Create_SessionItem_In_DocumentDB()
        {
            var docClientMoq = new Mock<CosmosClient>();
            var spResponse = CreateStoredProcedureResponseInstance<object>(HttpStatusCode.OK, null);
            object[] ssData = null;

            var databaseMoq = new Mock<Database>();
            SetupDatabaseMock(databaseMoq);

            docClientMoq.Setup(client => client.GetDatabase(DatabaseId))
                        .Returns(databaseMoq.Object);

            var containerMoq = new Mock<Container>();

            containerMoq.Setup(container => container.Scripts.ExecuteStoredProcedureAsync<object>(
                            CreateSessionStateItemSPID,
                            It.IsAny<PartitionKey>(),
                            It.Is<object[]>(parameters => parameters.Length == 5),
                            null,
                            default))
                        .Returns(Task.FromResult(spResponse))
                        .Callback<string, PartitionKey, object[], StoredProcedureRequestOptions, CancellationToken>((_, __, parameters, ___, ____) => ssData = parameters);

            docClientMoq.Setup(client => client.GetContainer(DatabaseId, ContainerId))
                        .Returns(containerMoq.Object);

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
            var docClientMoq = new Mock<CosmosClient>();
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

            var databaseMoq = new Mock<Database>();
            SetupDatabaseMock(databaseMoq);

            docClientMoq.Setup(client => client.GetDatabase(DatabaseId))
                        .Returns(databaseMoq.Object);

            var containerMoq = new Mock<Container>();

            containerMoq.Setup(container => container.Scripts.ExecuteStoredProcedureAsync<SessionStateItem>(
                            GetStateItemSPID,
                            It.IsAny<PartitionKey>(),
                            It.Is<object[]>(parameters => (string)parameters[0] == TestSessionId),
                            null,
                            default))
                        .Returns(Task.FromResult(spResponse));

            docClientMoq.Setup(client => client.GetContainer(DatabaseId, ContainerId))
                        .Returns(containerMoq.Object);

            var provider = CreateAndInitializeProviderWithDefaultConfig((_, __, ___) => docClientMoq.Object, compressionEnabled);
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
            var docClientMoq = new Mock<CosmosClient>();

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

            var databaseMoq = new Mock<Database>();
            SetupDatabaseMock(databaseMoq);

            docClientMoq.Setup(client => client.GetDatabase(DatabaseId))
                        .Returns(databaseMoq.Object);

            var containerMoq = new Mock<Container>();

            containerMoq.Setup(container => container.Scripts.ExecuteStoredProcedureAsync<SessionStateItem>(
                            GetStateItemSPID,
                            It.IsAny<PartitionKey>(),
                            It.Is<object[]>(parameters => (string)parameters[0] == TestSessionId),
                            null,
                            default))
                        .Returns(Task.FromResult(spResponse));

            docClientMoq.Setup(client => client.GetContainer(DatabaseId, ContainerId))
                        .Returns(containerMoq.Object);

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
            var docClientMoq = new Mock<CosmosClient>();
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
                Actions = SessionStateActions.None,
                Locked = false,
                SessionItem = Convert.FromBase64String(buff),
                LockAge = TimeSpan.Zero,
                LockCookie = DefaultLockCookie,
                Timeout = DefaultSessionTimeoutInSec
            };

            var spResponse = CreateStoredProcedureResponseInstance<SessionStateItem>(HttpStatusCode.OK, expectedSSItem);

            var databaseMoq = new Mock<Database>();
            SetupDatabaseMock(databaseMoq);

            docClientMoq.Setup(client => client.GetDatabase(DatabaseId))
                        .Returns(databaseMoq.Object);

            var containerMoq = new Mock<Container>();

            containerMoq.Setup(container => container.Scripts.ExecuteStoredProcedureAsync<SessionStateItem>(
                            GetStateItemExclusiveSPID,
                            It.IsAny<PartitionKey>(),
                            It.Is<object[]>(parameters => (string)parameters[0] == TestSessionId),
                            null,
                            default))
                        .Returns(Task.FromResult(spResponse));

            docClientMoq.Setup(client => client.GetContainer(DatabaseId, ContainerId))
                        .Returns(containerMoq.Object);

            var provider = CreateAndInitializeProviderWithDefaultConfig((_, __, ___) => docClientMoq.Object, true);
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
            var docClientMoq = new Mock<CosmosClient>();
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

            var databaseMoq = new Mock<Database>();
            SetupDatabaseMock(databaseMoq);

            docClientMoq.Setup(client => client.GetDatabase(DatabaseId))
                        .Returns(databaseMoq.Object);

            var containerMoq = new Mock<Container>();

            containerMoq.Setup(container => container.Scripts.ExecuteStoredProcedureAsync<SessionStateItem>(
                            GetStateItemExclusiveSPID,
                            It.IsAny<PartitionKey>(),
                            It.Is<object[]>(parameters => (string)parameters[0] == TestSessionId),
                            null,
                            default))
                        .Returns(Task.FromResult(spResponse));

            docClientMoq.Setup(client => client.GetContainer(DatabaseId, ContainerId))
                        .Returns(containerMoq.Object);

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
            var docClientMoq = new Mock<CosmosClient>();
            var spResponse = CreateStoredProcedureResponseInstance<object>(HttpStatusCode.OK, null);
            var lockId = 10;

            var databaseMoq = new Mock<Database>();
            SetupDatabaseMock(databaseMoq);

            docClientMoq.Setup(client => client.GetDatabase(DatabaseId))
                        .Returns(databaseMoq.Object);

            var containerMoq = new Mock<Container>();

            containerMoq.Setup(container => container.Scripts.ExecuteStoredProcedureAsync<object>(
                            ReleaseItemExclusiveSPID,
                            It.IsAny<PartitionKey>(),
                            It.Is<object[]>(parameters => (string)parameters[0] == TestSessionId && (int)parameters[1] == lockId),
                            null,
                            default))
                        .Returns(Task.FromResult(spResponse));

            docClientMoq.Setup(client => client.GetContainer(DatabaseId, ContainerId))
                        .Returns(containerMoq.Object);

            var provider = CreateAndInitializeProviderWithDefaultConfig((_, __, ___) => docClientMoq.Object);
            var httpContext = CreateMoqHttpContextBase();

            var exception = await Record.ExceptionAsync(
                async () => await provider.ReleaseItemExclusiveAsync(httpContext, TestSessionId, lockId, CancellationToken.None));

            Assert.Null(exception);
        }

        [Fact]
        public async void RemoveItemAsync_Should_Remove_SessionItem()
        {
            var docClientMoq = new Mock<CosmosClient>();
            var spResponse = CreateStoredProcedureResponseInstance<object>(HttpStatusCode.OK, null);
            var lockId = 10;

            var databaseMoq = new Mock<Database>();
            SetupDatabaseMock(databaseMoq);

            docClientMoq.Setup(client => client.GetDatabase(DatabaseId))
                        .Returns(databaseMoq.Object);

            var containerMoq = new Mock<Container>();

            containerMoq.Setup(container => container.Scripts.ExecuteStoredProcedureAsync<object>(
                            RemoveStateItemSPID,
                            It.IsAny<PartitionKey>(),
                            It.Is<object[]>(parameters => (string)parameters[0] == TestSessionId && (int)parameters[1] == lockId),
                            null,
                            default))
                        .Returns(Task.FromResult(spResponse));

            docClientMoq.Setup(client => client.GetContainer(DatabaseId, ContainerId))
                        .Returns(containerMoq.Object);

            var provider = CreateAndInitializeProviderWithDefaultConfig((_, __, ___) => docClientMoq.Object);
            var httpContext = CreateMoqHttpContextBase();

            var exception = await Record.ExceptionAsync(
                async () => await provider.RemoveItemAsync(httpContext, TestSessionId, lockId, null, CancellationToken.None));

            Assert.Null(exception);
        }

        [Fact]
        public async void ResetItemTimeoutAsync_Should_Reset_SessionItem_Timeout()
        {
            var docClientMoq = new Mock<CosmosClient>();
            var spResponse = CreateStoredProcedureResponseInstance<object>(HttpStatusCode.OK, null);

            var databaseMoq = new Mock<Database>();
            SetupDatabaseMock(databaseMoq);

            docClientMoq.Setup(client => client.GetDatabase(DatabaseId))
                        .Returns(databaseMoq.Object);

            var containerMoq = new Mock<Container>();

            containerMoq.Setup(container => container.Scripts.ExecuteStoredProcedureAsync<object>(
                            ResetItemTimeoutSPID,
                            It.IsAny<PartitionKey>(),
                            It.Is<object[]>(parameters => (string)parameters[0] == TestSessionId),
                            null,
                            default))
                        .Returns(Task.FromResult(spResponse));

            docClientMoq.Setup(client => client.GetContainer(DatabaseId, ContainerId))
                        .Returns(containerMoq.Object);

            var provider = CreateAndInitializeProviderWithDefaultConfig((_, __, ___) => docClientMoq.Object);
            var httpContext = CreateMoqHttpContextBase();

            var exception = await Record.ExceptionAsync(
                async () => await provider.ResetItemTimeoutAsync(httpContext, TestSessionId, CancellationToken.None));

            Assert.Null(exception);
        }

        [Fact]
        public async void SetAndReleaseItemExclusiveAsync_Should_Create_New_SessionItem_If_Session_Is_New()
        {
            var docClientMoq = new Mock<CosmosClient>();
            var spResponse = CreateStoredProcedureResponseInstance<object>(HttpStatusCode.OK, null);
            var sessionCollection = new SessionStateItemCollection();
            var now = DateTime.UtcNow;
            sessionCollection["test1"] = "test1";
            sessionCollection["test2"] = now;
            var data = new SessionStateStoreData(sessionCollection, new HttpStaticObjectsCollection(), DefaultSessionTimeout);
            object[] ssData = null;

            var databaseMoq = new Mock<Database>();
            SetupDatabaseMock(databaseMoq);

            docClientMoq.Setup(client => client.GetDatabase(DatabaseId))
                        .Returns(databaseMoq.Object);

            var containerMoq = new Mock<Container>();

            containerMoq.Setup(container => container.Scripts.ExecuteStoredProcedureAsync<object>(
                            CreateSessionStateItemSPID,
                            It.IsAny<PartitionKey>(),
                            It.Is<object[]>(parameters => parameters.Length == 5),
                            null,
                            default))
                        .Returns(Task.FromResult(spResponse))
                        .Callback<string, PartitionKey, object[], StoredProcedureRequestOptions, CancellationToken>((_, __, parameters, ___, ____) => ssData = parameters);

            docClientMoq.Setup(client => client.GetContainer(DatabaseId, ContainerId))
                        .Returns(containerMoq.Object);

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
            var docClientMoq = new Mock<CosmosClient>();
            var spResponse = CreateStoredProcedureResponseInstance<object>(HttpStatusCode.OK, null);
            var sessionCollection = new SessionStateItemCollection();
            var now = DateTime.UtcNow;
            sessionCollection["test1"] = "test1";
            sessionCollection["test2"] = now;
            var data = new SessionStateStoreData(sessionCollection, new HttpStaticObjectsCollection(), DefaultSessionTimeout);
            object[] ssData = null;

            var databaseMoq = new Mock<Database>();
            SetupDatabaseMock(databaseMoq);

            docClientMoq.Setup(client => client.GetDatabase(DatabaseId))
                        .Returns(databaseMoq.Object);

            var containerMoq = new Mock<Container>();

            containerMoq.Setup(container => container.Scripts.ExecuteStoredProcedureAsync<object>(
                            UpdateSessionStateItemSPID,
                            It.IsAny<PartitionKey>(),
                            It.Is<object[]>(parameters => parameters.Length == 4),
                            null,
                            default))
                        .Returns(Task.FromResult(spResponse))
                        .Callback<string, PartitionKey, object[], StoredProcedureRequestOptions, CancellationToken>((_, __, parameters, ___, ____) => ssData = parameters);

            docClientMoq.Setup(client => client.GetContainer(DatabaseId, ContainerId))
                        .Returns(containerMoq.Object);

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
            Func<string, string, CosmosClientOptions, CosmosClient> clientFactory, bool compressionEnabled = false)
        {
            CosmosDBSessionStateProviderAsync.ResetStaticFields();
            var provider = new CosmosDBSessionStateProviderAsync();
            provider.AppId = AppId;
            provider.CosmosClientFactory = clientFactory;

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
            providerConfig["containerId"] = ContainerId;

            provider.Initialize(DefaultProviderName, providerConfig, ssc, appSettings);

            return provider;
        }

        private CosmosException CreateDocumentClientException(HttpStatusCode statusCode = HttpStatusCode.NotFound)
        {
            return new CosmosException("", statusCode, 0, "", 0);
        }

        private void SetupReadSPSucessMock(Mock<Container> docClientMoq, string spId, Action callback)
        {
            docClientMoq.Setup(client => client.Scripts.ReadStoredProcedureAsync(spId, null, default))
                .Returns(Task.FromResult((StoredProcedureResponse)null))
                .Callback(callback);
        }

        private void SetupReadSPFailureMock(Mock<Container> docClientMoq, string spId, CosmosException ex)
        {
            docClientMoq.Setup(client => client.Scripts.ReadStoredProcedureAsync(spId, null, default)).Throws(ex);
        }

        private void SetupCreateSPMock(Mock<Container> docClientMoq, string spId, Action callback)
        {
            docClientMoq.Setup(client => client.Scripts.CreateStoredProcedureAsync(
                            It.Is<StoredProcedureProperties>(properties => properties.Id == spId), null, default))
                .Returns(Task.FromResult((StoredProcedureResponse)null))
                .Callback(callback);
        }

        private void SetupDatabaseMock(Mock<Database> databaseMoq, string id = ContainerId, string partitionKeyPath = DefaultPartitionKeyPath)
        {
            var containerResponseMoq = new Mock<ContainerResponse>();
            var existingContainerProperties = new ContainerProperties
            {
                Id = id,
                PartitionKeyPath = partitionKeyPath,
                DefaultTimeToLive = DefaultSessionTimeoutInSec
            };

            databaseMoq.Setup(database => database.CreateContainerIfNotExistsAsync(
                           It.IsAny<ContainerProperties>(), It.IsAny<int>(),
                           It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync((ContainerProperties props, int thru, RequestOptions opts, CancellationToken ct) => {
                           if (props.Id == id)
                               containerResponseMoq.SetupGet(cr => cr.Resource).Returns(existingContainerProperties);
                           else
                               containerResponseMoq.SetupGet(cr => cr.Resource).Returns(props);
                           return (ContainerResponse)containerResponseMoq.Object;
                       });
        }

        private StoredProcedureExecuteResponse<T> CreateStoredProcedureResponseInstance<T>(HttpStatusCode statusCode, T responseBody)
        {
            var spResponse = new Mock<StoredProcedureExecuteResponse<T>>();
            spResponse.Setup(response => response.StatusCode).Returns(statusCode);
            spResponse.Setup(response => response.Resource).Returns(responseBody);

            return spResponse.Object;
        }
    }
}
