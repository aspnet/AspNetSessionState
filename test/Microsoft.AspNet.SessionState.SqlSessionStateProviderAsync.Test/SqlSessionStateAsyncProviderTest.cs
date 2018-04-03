// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.AspNet.SessionState.SqlSessionStateAsyncProvider.Test
{
    using Moq;
    using System;
    using System.Collections.Specialized;
    using System.Configuration;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web;
    using System.Web.Configuration;
    using System.Web.SessionState;
    using Xunit;

    public class SqlSessionStateAsyncProviderTest
    {
        private const string InMemoryTableConfigurationName = "UseInMemoryTable";
        private const string MaxRetryConfigurationName = "MaxRetryNumber";
        private const string RetryIntervalConfigurationName = "RetryInterval";
        private const string DefaultTestConnectionString = "Server=myServerAddress;Database=myDataBase;User Id=myUsername;Password=myPassword;";
        private const string DefaultProviderName = "testprovider";
        private const string TableName = "ASPStateTempSessions";
        private const string AppId = "TestWebsite";
        private const string TestSessionId = "piqhlifa30ooedcp1k42mtef";
        private const int IdLength = 88;
        private const int TestTimeout = 10;
        private const int DefaultItemLength = 7000;
        private const int DefaultSqlCommandTimeout = 30;
        private const int DefaultRetryInterval = 1000;
        private const int DefaultRetryNum = 10;
        private const int DefaultInMemoryTableRetryNum = 1;

        public SqlSessionStateAsyncProviderTest()
        {
            SqlSessionStateProviderAsync.GetSessionStaticObjects = cxt => new HttpStaticObjectsCollection();
        }

        [Fact]
        public void Initialize_With_DefaultConfig_Should_Use_SqlSessionStateRepository_With_DefaultSettings()
        {
            var provider = CreateProvider();
            provider.Initialize(DefaultProviderName, CreateSessionStateProviderConfig(), CreateSessionStateSection(), 
                createConnectionStringSettings());

            Assert.Equal(DefaultProviderName, provider.Name);
            Assert.IsType<SqlSessionStateRepository>(provider.SqlSessionStateRepository);
            Assert.False(provider.CompressionEnabled);

            var repo = (SqlSessionStateRepository)provider.SqlSessionStateRepository;
            Assert.Equal(DefaultTestConnectionString, repo.ConnectString);
            Assert.Equal(DefaultSqlCommandTimeout, repo.CommandTimeout);
            Assert.Equal(DefaultRetryInterval, repo.RetryIntervalMilSec);
            Assert.Equal(DefaultRetryNum, repo.MaxRetryNum);
        }

        [Fact]
        public void Initialize_With_SqlSessionStateRepositorySettings_Should_Use_Configured_Settings()
        {
            var provider = CreateProvider();
            provider.Initialize(DefaultProviderName, CreateSessionStateProviderConfig(useInMemoryTable: "false", retryInterval: "99", maxRetryNum: "9"), 
                CreateSessionStateSection(timeoutInSecond: 88, compressionEnabled: true),
                createConnectionStringSettings());

            Assert.Equal(DefaultProviderName, provider.Name);
            Assert.IsType<SqlSessionStateRepository>(provider.SqlSessionStateRepository);
            Assert.True(provider.CompressionEnabled);

            var repo = (SqlSessionStateRepository)provider.SqlSessionStateRepository;
            Assert.Equal(DefaultTestConnectionString, repo.ConnectString);
            Assert.Equal(88, repo.CommandTimeout);
            Assert.Equal(99, repo.RetryIntervalMilSec);
            Assert.Equal(9, repo.MaxRetryNum);
        }

        [Fact]
        public void Initialize_With_InMemoryTableDefaultConfig_Should_Use_SqlInMemoryTableSessionStateRepository_With_DefaultSettings()
        {
            var provider = CreateProvider();
            provider.Initialize(DefaultProviderName, CreateSessionStateProviderConfig(useInMemoryTable: "true"), 
                CreateSessionStateSection(), createConnectionStringSettings());

            Assert.Equal(DefaultProviderName, provider.Name);
            Assert.IsType<SqlInMemoryTableSessionStateRepository>(provider.SqlSessionStateRepository);
            Assert.False(provider.CompressionEnabled);

            var repo = (SqlInMemoryTableSessionStateRepository)provider.SqlSessionStateRepository;
            Assert.Equal(DefaultTestConnectionString, repo.ConnectString);
            Assert.Equal(DefaultSqlCommandTimeout, repo.CommandTimeout);
            Assert.Equal(DefaultInMemoryTableRetryNum, repo.RetryIntervalMilSec);
            Assert.Equal(DefaultRetryNum, repo.MaxRetryNum);
        }

        [Fact]
        public void Initialize_With_SqlInMemoryTableSessionStateRepositorySettings_Should_Use_Configured_Settings()
        {
            var provider = CreateProvider();
            provider.Initialize(DefaultProviderName, CreateSessionStateProviderConfig(useInMemoryTable: "true", retryInterval: "99", maxRetryNum: "9"),
                CreateSessionStateSection(timeoutInSecond: 88, compressionEnabled: true),
                createConnectionStringSettings());

            Assert.Equal(DefaultProviderName, provider.Name);
            Assert.IsType<SqlInMemoryTableSessionStateRepository>(provider.SqlSessionStateRepository);
            Assert.True(provider.CompressionEnabled);

            var repo = (SqlInMemoryTableSessionStateRepository)provider.SqlSessionStateRepository;
            Assert.Equal(DefaultTestConnectionString, repo.ConnectString);
            Assert.Equal(88, repo.CommandTimeout);
            Assert.Equal(99, repo.RetryIntervalMilSec);
            Assert.Equal(9, repo.MaxRetryNum);
        }

        [Fact]
        public void CreateNewStoreData_Should_Return_Empty_Store()
        {
            var provider = CreateProvider();
            provider.Initialize(DefaultProviderName, CreateSessionStateProviderConfig(), CreateSessionStateSection(),
                createConnectionStringSettings());

            var store = provider.CreateNewStoreData(null, TestTimeout);
            Assert.Equal(0, store.Items.Count);
            Assert.Null(store.StaticObjects);
            Assert.Equal(TestTimeout, store.Timeout);
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
            var data = new SessionStateStoreData(sessionCollection, new HttpStaticObjectsCollection(), TestTimeout);

            byte[] buff;
            int length;
            SessionStateStoreData deserializedData;

            SqlSessionStateProviderAsync.SerializeStoreData(data, DefaultItemLength, out buff, out length, enableCompression);
            using (var stream = new MemoryStream(buff))
            {
                var httpContext = CreateMoqHttpContextBase();
                deserializedData = SqlSessionStateProviderAsync.DeserializeStoreData(httpContext, stream, enableCompression);
            }

            Assert.Equal(data.Items.Count, deserializedData.Items.Count);
            Assert.Equal(data.Items["test1"], deserializedData.Items["test1"]);
            Assert.Equal(now, (DateTime)deserializedData.Items["test2"]);
            Assert.NotNull(deserializedData.StaticObjects);
            Assert.Equal(data.Timeout, deserializedData.Timeout);
        }

        [Fact]
        public async void CreateUninitializedItem_Should_CreateUninitializedItem_In_Repository()
        {
            var provider = CreateProvider();
            provider.Initialize(DefaultProviderName, CreateSessionStateProviderConfig(), CreateSessionStateSection(),
                createConnectionStringSettings());

            var repoMoq = new Mock<ISqlSessionStateRepository>();
            string sessionId = "";
            int length = 0;
            byte[] buff = null;
            int timeout = 0;
            repoMoq.Setup(repo => repo.CreateUninitializedSessionItemAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<byte[]>(), It.IsAny<int>()))
                .Returns(Task.CompletedTask).Callback<string, int, byte[], int>((id, len, bytes, to) => { sessionId = id; length = len; buff = bytes; timeout = to; });

            provider.SqlSessionStateRepository = repoMoq.Object;
            var httpContextBase = CreateMoqHttpContextBase();

            var expectedTimeout = 60;
            await provider.CreateUninitializedItemAsync(httpContextBase, TestSessionId, expectedTimeout, CancellationToken.None);

            Assert.Equal(SqlSessionStateProviderAsync.AppendAppIdHash(TestSessionId), sessionId);
            Assert.Equal(expectedTimeout, timeout);
            Assert.NotNull(buff);
            Assert.Equal(7, length);
        }

        [Theory]
        [InlineData(SessionStateActions.None)]
        [InlineData(SessionStateActions.InitializeItem)]
        public async void GetItemAsync_Should_Return_SessionItem_If_SessionItem_Is_Unlocked(SessionStateActions action)
        {
            var provider = CreateProvider();
            provider.Initialize(DefaultProviderName, CreateSessionStateProviderConfig(), CreateSessionStateSection(),
                createConnectionStringSettings());

            var sessionCollection = new SessionStateItemCollection();
            var now = DateTime.UtcNow;
            sessionCollection["test1"] = "test1";
            sessionCollection["test2"] = now;
            var data = new SessionStateStoreData(sessionCollection, new HttpStaticObjectsCollection(), TestTimeout);
            byte[] buff;
            int length;
            SqlSessionStateProviderAsync.SerializeStoreData(data, DefaultItemLength, out buff, out length, false);

            var repoMoq = new Mock<ISqlSessionStateRepository>();
            var ssItem = new SessionItem(buff, false, TimeSpan.Zero, null, action);
            repoMoq.Setup(repo => repo.GetSessionStateItemAsync(SqlSessionStateProviderAsync.AppendAppIdHash(TestSessionId), false)).Returns(Task.FromResult(ssItem));
            provider.SqlSessionStateRepository = repoMoq.Object;
            var httpContext = CreateMoqHttpContextBase();

            var getItemResult = await provider.GetItemAsync(httpContext, TestSessionId, CancellationToken.None);

            Assert.Equal(data.Items.Count, getItemResult.Item.Items.Count);
            Assert.Equal(data.Items["test1"], getItemResult.Item.Items["test1"]);
            Assert.Equal(now, (DateTime)getItemResult.Item.Items["test2"]);
            Assert.NotNull(getItemResult.Item.StaticObjects);
            Assert.Equal(data.Timeout, getItemResult.Item.Timeout);
            Assert.Equal(ssItem.Actions, getItemResult.Actions);
            Assert.Equal(ssItem.LockAge, getItemResult.LockAge);
            Assert.Equal(ssItem.Locked, getItemResult.Locked);
            Assert.Equal(ssItem.LockId, getItemResult.LockId);
            Assert.NotEqual(0, provider.OrigStreamLen);
        }

        [Fact]
        public async void GetItemAsync_Should_Return_SessionItem_With_Null_IF_SessionItem_Is_Locked()
        {
            var provider = CreateProvider();
            provider.Initialize(DefaultProviderName, CreateSessionStateProviderConfig(), CreateSessionStateSection(),
                createConnectionStringSettings());

            var repoMoq = new Mock<ISqlSessionStateRepository>();
            var ssItem = new SessionItem(null, true, TimeSpan.FromSeconds(2), 1, SessionStateActions.None);
            repoMoq.Setup(repo => repo.GetSessionStateItemAsync(SqlSessionStateProviderAsync.AppendAppIdHash(TestSessionId), false)).Returns(Task.FromResult(ssItem));
            provider.SqlSessionStateRepository = repoMoq.Object;
            var httpContext = CreateMoqHttpContextBase();

            var getItemResult = await provider.GetItemAsync(httpContext, TestSessionId, CancellationToken.None);

            Assert.Null(getItemResult.Item);
            Assert.Equal(ssItem.Actions, getItemResult.Actions);
            Assert.Equal(ssItem.LockAge, getItemResult.LockAge);
            Assert.Equal(ssItem.Locked, getItemResult.Locked);
            Assert.Equal(ssItem.LockId, getItemResult.LockId);
            Assert.Equal(0, provider.OrigStreamLen);
        }

        [Fact]
        public async void GetItemExclusiveAsync_Should_Return_SessionItem_If_SessionItem_Is_Unlocked()
        {
            var provider = CreateProvider();
            provider.Initialize(DefaultProviderName, CreateSessionStateProviderConfig(), CreateSessionStateSection(),
                createConnectionStringSettings());

            var sessionCollection = new SessionStateItemCollection();
            var now = DateTime.UtcNow;
            sessionCollection["test1"] = "test1";
            sessionCollection["test2"] = now;
            var data = new SessionStateStoreData(sessionCollection, new HttpStaticObjectsCollection(), TestTimeout);

            byte[] buff;
            int length;
            SqlSessionStateProviderAsync.SerializeStoreData(data, DefaultItemLength, out buff, out length, false);

            var repoMoq = new Mock<ISqlSessionStateRepository>();
            var ssItem = new SessionItem(buff, true, TimeSpan.Zero, 1, SessionStateActions.None);
            repoMoq.Setup(repo => repo.GetSessionStateItemAsync(SqlSessionStateProviderAsync.AppendAppIdHash(TestSessionId), true)).Returns(Task.FromResult(ssItem));
            provider.SqlSessionStateRepository = repoMoq.Object;
            var httpContext = CreateMoqHttpContextBase();

            var getItemResult = await provider.GetItemExclusiveAsync(httpContext, TestSessionId, CancellationToken.None);

            Assert.Equal(data.Items.Count, getItemResult.Item.Items.Count);
            Assert.Equal(data.Items["test1"], getItemResult.Item.Items["test1"]);
            Assert.Equal(now, (DateTime)getItemResult.Item.Items["test2"]);
            Assert.NotNull(getItemResult.Item.StaticObjects);
            Assert.Equal(data.Timeout, getItemResult.Item.Timeout);
            Assert.Equal(ssItem.Actions, getItemResult.Actions);
            Assert.Equal(ssItem.LockAge, getItemResult.LockAge);
            Assert.Equal(ssItem.Locked, getItemResult.Locked);
            Assert.Equal(ssItem.LockId, getItemResult.LockId);
            Assert.NotEqual(0, provider.OrigStreamLen);
        }

        [Fact]
        public async void GetItemExclusiveAsync_Should_Return_SessionItem_With_Null_IF_SessionItem_Is_Locked()
        {
            var provider = CreateProvider();
            provider.Initialize(DefaultProviderName, CreateSessionStateProviderConfig(), CreateSessionStateSection(),
                createConnectionStringSettings());

            var repoMoq = new Mock<ISqlSessionStateRepository>();
            var ssItem = new SessionItem(null, true, TimeSpan.FromSeconds(2), 1, SessionStateActions.None);
            repoMoq.Setup(repo => repo.GetSessionStateItemAsync(SqlSessionStateProviderAsync.AppendAppIdHash(TestSessionId), true)).Returns(Task.FromResult(ssItem));
            provider.SqlSessionStateRepository = repoMoq.Object;
            var httpContext = CreateMoqHttpContextBase();

            var getItemResult = await provider.GetItemExclusiveAsync(httpContext, TestSessionId, CancellationToken.None);

            Assert.Null(getItemResult.Item);
            Assert.Equal(ssItem.Actions, getItemResult.Actions);
            Assert.Equal(ssItem.LockAge, getItemResult.LockAge);
            Assert.Equal(ssItem.Locked, getItemResult.Locked);
            Assert.Equal(ssItem.LockId, getItemResult.LockId);
            Assert.Equal(0, provider.OrigStreamLen);
        }

        [Fact]
        public async void InitializeRequest_Should_Reset_OrigStreamLen()
        {
            var provider = CreateProvider();
            provider.Initialize(DefaultProviderName, CreateSessionStateProviderConfig(), CreateSessionStateSection(),
                createConnectionStringSettings());
            Assert.Equal(0, provider.OrigStreamLen);

            var sessionCollection = new SessionStateItemCollection();
            var now = DateTime.UtcNow;
            sessionCollection["test1"] = "test1";
            sessionCollection["test2"] = now;
            var data = new SessionStateStoreData(sessionCollection, new HttpStaticObjectsCollection(), TestTimeout);
            byte[] buff;
            int length;
            SqlSessionStateProviderAsync.SerializeStoreData(data, DefaultItemLength, out buff, out length, false);

            var repoMoq = new Mock<ISqlSessionStateRepository>();
            var ssItem = new SessionItem(buff, true, TimeSpan.Zero, 1, SessionStateActions.None);
            repoMoq.Setup(repo => repo.GetSessionStateItemAsync(SqlSessionStateProviderAsync.AppendAppIdHash(TestSessionId), false)).Returns(Task.FromResult(ssItem));
            provider.SqlSessionStateRepository = repoMoq.Object;
            var httpContext = CreateMoqHttpContextBase();

            var getItemResult = await provider.GetItemAsync(httpContext, TestSessionId, CancellationToken.None);
            Assert.NotEqual(0, provider.OrigStreamLen);

            provider.InitializeRequest(httpContext);
            Assert.Equal(0, provider.OrigStreamLen);
        }

        [Fact]
        public async void ReleaseItemExclusiveAsync_Should_Unlock_SessionItem()
        {
            var provider = CreateProvider();
            provider.Initialize(DefaultProviderName, CreateSessionStateProviderConfig(), CreateSessionStateSection(),
                createConnectionStringSettings());

            var repoMoq = new Mock<ISqlSessionStateRepository>();
            var isLocked = true;
            repoMoq.Setup(repo => repo.ReleaseSessionItemAsync(SqlSessionStateProviderAsync.AppendAppIdHash(TestSessionId), 1)).Returns(Task.CompletedTask)
                .Callback(() => isLocked = false);
            provider.SqlSessionStateRepository = repoMoq.Object;
            var httpContext = CreateMoqHttpContextBase();

            await provider.ReleaseItemExclusiveAsync(httpContext, TestSessionId, 1, CancellationToken.None);

            Assert.False(isLocked);
        }

        [Fact]
        public async void RemoveItemAsync_Should_Remove_SessionItem()
        {
            var provider = CreateProvider();
            provider.Initialize(DefaultProviderName, CreateSessionStateProviderConfig(), CreateSessionStateSection(),
                createConnectionStringSettings());

            var repoMoq = new Mock<ISqlSessionStateRepository>();
            var isRemoved = false;
            repoMoq.Setup(repo => repo.RemoveSessionItemAsync(SqlSessionStateProviderAsync.AppendAppIdHash(TestSessionId), 1))
                .Returns(Task.CompletedTask)
                .Callback(() => isRemoved = true);
            provider.SqlSessionStateRepository = repoMoq.Object;
            var httpContext = CreateMoqHttpContextBase();

            await provider.RemoveItemAsync(httpContext, TestSessionId, 1, null, CancellationToken.None);

            Assert.True(isRemoved);
        }

        [Fact]
        public async void ResetItemTimeoutAsync_Should_Reset_SessionItem_Timeout()
        {
            var provider = CreateProvider();
            provider.Initialize(DefaultProviderName, CreateSessionStateProviderConfig(), CreateSessionStateSection(),
                createConnectionStringSettings());

            var repoMoq = new Mock<ISqlSessionStateRepository>();
            var timeout = 100;
            repoMoq.Setup(repo => repo.ResetSessionItemTimeoutAsync(SqlSessionStateProviderAsync.AppendAppIdHash(TestSessionId)))
                .Returns(Task.CompletedTask)
                .Callback(() => timeout = 200);
            provider.SqlSessionStateRepository = repoMoq.Object;
            var httpContext = CreateMoqHttpContextBase();

            await provider.ResetItemTimeoutAsync(httpContext, TestSessionId, CancellationToken.None);

            Assert.Equal(200, timeout);
        }

        [Fact]
        public async void SetAndReleaseItemExclusiveAsync_Should_Create_New_SessionItem_If_Session_Is_New()
        {
            var provider = CreateProvider();
            provider.Initialize(DefaultProviderName, CreateSessionStateProviderConfig(), CreateSessionStateSection(),
                createConnectionStringSettings());

            var sessionCollection = new SessionStateItemCollection();
            var now = DateTime.UtcNow;
            sessionCollection["test1"] = "test1";
            sessionCollection["test2"] = now;
            var data = new SessionStateStoreData(sessionCollection, new HttpStaticObjectsCollection(), TestTimeout);
            byte[] buff;
            int length;
            SqlSessionStateProviderAsync.SerializeStoreData(data, DefaultItemLength, out buff, out length, false);

            var repoMoq = new Mock<ISqlSessionStateRepository>();
            var sessionItemCreated = false;
            repoMoq.Setup(repo => repo.CreateOrUpdateSessionStateItemAsync(true, SqlSessionStateProviderAsync.AppendAppIdHash(TestSessionId), 
                                                                            buff, length, TestTimeout, 0, 0))
                    .Returns(Task.CompletedTask)
                    .Callback(() => sessionItemCreated = true);
            provider.SqlSessionStateRepository = repoMoq.Object;
            var httpContext = CreateMoqHttpContextBase();

            await provider.SetAndReleaseItemExclusiveAsync(httpContext, TestSessionId, data, null, true, CancellationToken.None);

            Assert.True(sessionItemCreated);
        }

        [Fact]
        public async void SetAndReleaseItemExclusiveAsync_Should_Release_NonExclsive_SessionItem()
        {
            var provider = CreateProvider();
            provider.Initialize(DefaultProviderName, CreateSessionStateProviderConfig(), CreateSessionStateSection(),
                createConnectionStringSettings());

            var sessionCollection = new SessionStateItemCollection();
            var now = DateTime.UtcNow;
            sessionCollection["test1"] = "test1";
            sessionCollection["test2"] = now;
            var data = new SessionStateStoreData(sessionCollection, new HttpStaticObjectsCollection(), TestTimeout);
            byte[] buff;
            int length;
            SqlSessionStateProviderAsync.SerializeStoreData(data, DefaultItemLength, out buff, out length, false);

            var repoMoq = new Mock<ISqlSessionStateRepository>();
            var ssItem = new SessionItem(buff, false, TimeSpan.Zero, null, SessionStateActions.None);
            repoMoq.Setup(repo => repo.GetSessionStateItemAsync(SqlSessionStateProviderAsync.AppendAppIdHash(TestSessionId), false))
                    .Returns(Task.FromResult(ssItem));
            provider.SqlSessionStateRepository = repoMoq.Object;
            var httpContext = CreateMoqHttpContextBase();

            var getItemResult = await provider.GetItemAsync(httpContext, TestSessionId, CancellationToken.None);
            var sessionReleased = false;
            repoMoq.Setup(repo => repo.CreateOrUpdateSessionStateItemAsync(false, SqlSessionStateProviderAsync.AppendAppIdHash(TestSessionId), 
                                                                            buff, length, TestTimeout, 0, provider.OrigStreamLen))
                    .Returns(Task.CompletedTask)
                    .Callback(() => sessionReleased = true);
                        
            await provider.SetAndReleaseItemExclusiveAsync(httpContext, TestSessionId, getItemResult.Item, null, false, CancellationToken.None);

            Assert.True(sessionReleased);
        }

        [Fact]
        public async void SetAndReleaseItemExclusiveAsync_Should_Release_Exclsive_SessionItem()
        {
            var provider = CreateProvider();
            provider.Initialize(DefaultProviderName, CreateSessionStateProviderConfig(), CreateSessionStateSection(),
                                createConnectionStringSettings());

            var sessionCollection = new SessionStateItemCollection();
            var now = DateTime.UtcNow;
            sessionCollection["test1"] = "test1";
            sessionCollection["test2"] = now;
            var data = new SessionStateStoreData(sessionCollection, new HttpStaticObjectsCollection(), TestTimeout);

            byte[] buff;
            int length;
            SqlSessionStateProviderAsync.SerializeStoreData(data, DefaultItemLength, out buff, out length, false);

            var repoMoq = new Mock<ISqlSessionStateRepository>();
            var ssItem = new SessionItem(buff, true, TimeSpan.Zero, 1, SessionStateActions.None);
            repoMoq.Setup(repo => repo.GetSessionStateItemAsync(SqlSessionStateProviderAsync.AppendAppIdHash(TestSessionId), true))
                .Returns(Task.FromResult(ssItem));
            provider.SqlSessionStateRepository = repoMoq.Object;
            var httpContext = CreateMoqHttpContextBase();

            var getItemResult = await provider.GetItemExclusiveAsync(httpContext, TestSessionId, CancellationToken.None);
            var sessionReleased = false;
            repoMoq.Setup(repo => repo.CreateOrUpdateSessionStateItemAsync(false, SqlSessionStateProviderAsync.AppendAppIdHash(TestSessionId), buff, length, TestTimeout, (int)ssItem.LockId, provider.OrigStreamLen))
                    .Returns(Task.CompletedTask)
                    .Callback(() => sessionReleased = true);

            await provider.SetAndReleaseItemExclusiveAsync(httpContext, TestSessionId, getItemResult.Item, getItemResult.LockId, false, CancellationToken.None);

            Assert.True(sessionReleased);
        }

        private SqlSessionStateProviderAsync CreateProvider()
        {
            var provider = new SqlSessionStateProviderAsync();
            provider.AppId = AppId;
            provider.ResetOneTimeInited();
            return provider;
        }

        private HttpContextBase CreateMoqHttpContextBase()
        {
            var httpContextBaseMoq = new Mock<HttpContextBase>();
            var httpAppMoq = new Mock<HttpApplication>();

            httpContextBaseMoq.SetupGet(cxtBase => cxtBase.ApplicationInstance).Returns(httpAppMoq.Object);

            return httpContextBaseMoq.Object;
        }

        private SessionStateSection CreateSessionStateSection(double? timeoutInSecond = null, bool? compressionEnabled = null)
        {
            var ssc = new SessionStateSection();
            if (timeoutInSecond.HasValue)
            {
                ssc.SqlCommandTimeout = TimeSpan.FromSeconds(timeoutInSecond.Value);
            }
            if (compressionEnabled.HasValue)
            {
                ssc.CompressionEnabled = compressionEnabled.Value;
            }

            return ssc;
        }

        private NameValueCollection CreateSessionStateProviderConfig(string useInMemoryTable = null, string retryInterval = null, string maxRetryNum = null)
        {
            var config = new NameValueCollection();

            config[InMemoryTableConfigurationName] = useInMemoryTable;
            config[MaxRetryConfigurationName] = maxRetryNum;
            config[RetryIntervalConfigurationName] = retryInterval;

            return config;
        }

        private ConnectionStringSettings createConnectionStringSettings(string connectStr = DefaultTestConnectionString)
        {
            var connectionStrSetting = new ConnectionStringSettings();
            connectionStrSetting.ConnectionString = connectStr;

            return connectionStrSetting;
        }
    }
}
