// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See the License.txt file in the project root for full license information.
#define XUNIT_SKIP

namespace Microsoft.AspNet.SessionState.SqlSessionStateAsyncProvider.Test
{
    using Microsoft.Data.SqlClient;
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
        private const string RepositoryTypeConfigurationName = "repositoryType";
        private const string TableNameConfigurationName = "sessionTableName";
        private const string InMemoryTableConfigurationName = "useInMemoryTable";
        private const string MaxRetryConfigurationName = "maxRetryNumber";
        private const string RetryIntervalConfigurationName = "retryInterval";
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
        private const int DefaultInMemoryTableRetryInterval = 1;

        public SqlSessionStateAsyncProviderTest()
        {
            SqlSessionStateProviderAsync.GetSessionStaticObjects = cxt => new HttpStaticObjectsCollection();
        }

        [Fact]
        public void Initialize_With_DefaultConfig_Should_Use_FxCompatSssionStateRepository_With_DefaultSettings()
        {
            var provider = CreateProvider();
            provider.Initialize(DefaultProviderName, CreateSessionStateProviderConfig(), CreateSessionStateSection(), 
                CreateConnectionStringSettings());

            Assert.Equal(DefaultProviderName, provider.Name);
            Assert.IsType<SqlFxCompatSessionStateRepository>(provider.SqlSessionStateRepository);
            Assert.False(provider.CompressionEnabled);

            var repo = (SqlFxCompatSessionStateRepository)provider.SqlSessionStateRepository;
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
                CreateConnectionStringSettings());

            Assert.Equal(DefaultProviderName, provider.Name);
            Assert.IsType<SqlFxCompatSessionStateRepository>(provider.SqlSessionStateRepository);
            Assert.True(provider.CompressionEnabled);

            var repo = (SqlFxCompatSessionStateRepository)provider.SqlSessionStateRepository;
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
                CreateSessionStateSection(), CreateConnectionStringSettings());

            Assert.Equal(DefaultProviderName, provider.Name);
            Assert.IsType<SqlInMemoryTableSessionStateRepository>(provider.SqlSessionStateRepository);
            Assert.False(provider.CompressionEnabled);

            var repo = (SqlInMemoryTableSessionStateRepository)provider.SqlSessionStateRepository;
            Assert.Equal(DefaultTestConnectionString, repo.ConnectString);
            Assert.Equal(DefaultSqlCommandTimeout, repo.CommandTimeout);
            Assert.Equal(DefaultInMemoryTableRetryInterval, repo.RetryIntervalMilSec);
            Assert.Equal(DefaultRetryNum, repo.MaxRetryNum);
            Assert.False(repo.UseDurableData);
        }

        [Fact]
        public void Initialize_With_SqlInMemoryTableSessionStateRepositorySettings_Should_Use_Configured_Settings()
        {
            var provider = CreateProvider();
            provider.Initialize(DefaultProviderName, CreateSessionStateProviderConfig(useInMemoryTable: "true", retryInterval: "99", maxRetryNum: "9"),
                CreateSessionStateSection(timeoutInSecond: 88, compressionEnabled: true),
                CreateConnectionStringSettings());

            Assert.Equal(DefaultProviderName, provider.Name);
            Assert.IsType<SqlInMemoryTableSessionStateRepository>(provider.SqlSessionStateRepository);
            Assert.True(provider.CompressionEnabled);

            var repo = (SqlInMemoryTableSessionStateRepository)provider.SqlSessionStateRepository;
            Assert.Equal(DefaultTestConnectionString, repo.ConnectString);
            Assert.Equal(88, repo.CommandTimeout);
            Assert.Equal(99, repo.RetryIntervalMilSec);
            Assert.Equal(9, repo.MaxRetryNum);
        }

        [Theory]
        [InlineData("SqlServer", typeof(SqlSessionStateRepository))]
        [InlineData("InMemory", typeof(SqlInMemoryTableSessionStateRepository))]
        [InlineData("InMemoryDurable", typeof(SqlInMemoryTableSessionStateRepository))]
        [InlineData("FrameworkCompat", typeof(SqlFxCompatSessionStateRepository))]
        public void Initialize_With_RepositoryType_SqlServer_Should_Use_CorrectRepository(string repoTypeString, Type repoType)
        {
            var provider = CreateProvider();
            provider.Initialize(DefaultProviderName, CreateSessionStateProviderConfig(repoType: repoTypeString),
                CreateSessionStateSection(), CreateConnectionStringSettings());

            Assert.Equal(DefaultProviderName, provider.Name);
            Assert.IsType(repoType, provider.SqlSessionStateRepository);
            Assert.False(provider.CompressionEnabled);

            RepositoryType repoTypeEnum = (RepositoryType)Enum.Parse(typeof(RepositoryType), repoTypeString);

            if (provider.SqlSessionStateRepository is SqlSessionStateRepository sqlRepo)
            {
                Assert.Equal(DefaultTestConnectionString, sqlRepo.ConnectString);
                Assert.Equal(DefaultSqlCommandTimeout, sqlRepo.CommandTimeout);
                Assert.Equal(DefaultRetryInterval, sqlRepo.RetryIntervalMilSec);
                Assert.Equal(DefaultRetryNum, sqlRepo.MaxRetryNum);
                Assert.Equal(GetDefaultTableName(repoTypeEnum), sqlRepo.SessionStateTableName);
            }
            else if (provider.SqlSessionStateRepository is SqlInMemoryTableSessionStateRepository inMemRepo)
            {
                Assert.Equal(DefaultTestConnectionString, inMemRepo.ConnectString);
                Assert.Equal(DefaultSqlCommandTimeout, inMemRepo.CommandTimeout);
                Assert.Equal(DefaultInMemoryTableRetryInterval, inMemRepo.RetryIntervalMilSec);
                Assert.Equal(DefaultRetryNum, inMemRepo.MaxRetryNum);
                Assert.Equal(repoTypeString == "InMemoryDurable", inMemRepo.UseDurableData);
                Assert.Equal(GetDefaultTableName(repoTypeEnum), inMemRepo.SessionStateTableName);
            }
            else if (provider.SqlSessionStateRepository is SqlFxCompatSessionStateRepository compatRepo)
            {
                Assert.Equal(DefaultTestConnectionString, compatRepo.ConnectString);
                Assert.Equal(DefaultSqlCommandTimeout, compatRepo.CommandTimeout);
                Assert.Equal(DefaultRetryInterval, compatRepo.RetryIntervalMilSec);
                Assert.Equal(DefaultRetryNum, compatRepo.MaxRetryNum);
                Assert.Equal(GetDefaultTableName(repoTypeEnum), compatRepo.SessionStateTableName);
            }
            else
            {
                // Should have matched one of the types above. Fail here
                Assert.True(false);
            }
        }

        [Theory]
        [InlineData("SqlServer", typeof(SqlSessionStateRepository))]
        [InlineData("InMemory", typeof(SqlInMemoryTableSessionStateRepository))]
        [InlineData("InMemoryDurable", typeof(SqlInMemoryTableSessionStateRepository))]
        [InlineData("FrameworkCompat", typeof(SqlFxCompatSessionStateRepository))]
        public void Initialize_With_RepositoryType_Should_Use_Should_Use_Configured_Settings(string repoTypeString, Type repoType)
        {
            var provider = CreateProvider();
            provider.Initialize(DefaultProviderName, CreateSessionStateProviderConfig(repoType: repoTypeString, useInMemoryTable: "true", retryInterval: "99", maxRetryNum: "9", tableName: "TestTableName"),
                CreateSessionStateSection(timeoutInSecond: 88, compressionEnabled: true),
                CreateConnectionStringSettings());

            Assert.Equal(DefaultProviderName, provider.Name);
            Assert.IsType(repoType, provider.SqlSessionStateRepository);
            Assert.True(provider.CompressionEnabled);

            if (provider.SqlSessionStateRepository is SqlSessionStateRepository sqlRepo)
            {
                Assert.Equal(DefaultTestConnectionString, sqlRepo.ConnectString);
                Assert.Equal(88, sqlRepo.CommandTimeout);
                Assert.Equal(99, sqlRepo.RetryIntervalMilSec);
                Assert.Equal(9, sqlRepo.MaxRetryNum);
                Assert.Equal("TestTableName", sqlRepo.SessionStateTableName);
            }
            else if (provider.SqlSessionStateRepository is SqlInMemoryTableSessionStateRepository inMemRepo)
            {
                Assert.Equal(DefaultTestConnectionString, inMemRepo.ConnectString);
                Assert.Equal(88, inMemRepo.CommandTimeout);
                Assert.Equal(99, inMemRepo.RetryIntervalMilSec);
                Assert.Equal(9, inMemRepo.MaxRetryNum);
                Assert.Equal("TestTableName", inMemRepo.SessionStateTableName); // No 'Durable' addition when using a custom table name.
                Assert.Equal(repoTypeString == "InMemoryDurable", inMemRepo.UseDurableData);
            }
            else if (provider.SqlSessionStateRepository is SqlFxCompatSessionStateRepository compatRepo)
            {
                Assert.Equal(DefaultTestConnectionString, compatRepo.ConnectString);
                Assert.Equal(88, compatRepo.CommandTimeout);
                Assert.Equal(99, compatRepo.RetryIntervalMilSec);
                Assert.Equal(9, compatRepo.MaxRetryNum);
                Assert.Equal("TestTableName", compatRepo.SessionStateTableName);
            }
            else
            {
                // Should have matched one of the types above. Fail here
                Assert.True(false);
            }
        }


        [Fact]
        public void CreateNewStoreData_Should_Return_Empty_Store()
        {
            var provider = CreateProvider();
            provider.Initialize(DefaultProviderName, CreateSessionStateProviderConfig(), CreateSessionStateSection(),
                CreateConnectionStringSettings());

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
                CreateConnectionStringSettings());

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
                CreateConnectionStringSettings());

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
                CreateConnectionStringSettings());

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
                CreateConnectionStringSettings());

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
                CreateConnectionStringSettings());

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
                CreateConnectionStringSettings());
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
                CreateConnectionStringSettings());

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
                CreateConnectionStringSettings());

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
                CreateConnectionStringSettings());

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
                CreateConnectionStringSettings());

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
                CreateConnectionStringSettings());

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
                                CreateConnectionStringSettings());

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

        // ==================================================================================================================================================
        //
        // Requires existing database & connection string.
        //   Disabled by default, but you can enable if you've got a DB ready for testing against.
        //   When using Azure SQL, 'InMemory' is only supported by the 'Premium' DTU or 'Business Critical' vCore tiers.
        //
        //
#if INCLUDE_ADVANCED_TESTS
        const string sqlServerConnStr = @"";

        [SkippableTheory]
        [InlineData(sqlServerConnStr, "SqlServer", null)]
        [InlineData(sqlServerConnStr, "SqlServer", "SSTestTable")]
        [InlineData(sqlServerConnStr, "InMemory", null)]
        [InlineData(sqlServerConnStr, "InMemory", "IMTestTable")]
        [InlineData(sqlServerConnStr, "InMemoryDurable", null)]
        [InlineData(sqlServerConnStr, "InMemoryDurable", "IMDTestTable")]
        public async void Deployment_CreatesNew_ReusesOld_Schema_And_SProcs(string connstr, string repoTypeString, string configuredTableName)
        {
            // Skip if no connection string.
            // Assert.Skip will work in XUnit V3, but the latest release is only 2.4.2. Use the 3rd party "Xunit.SkippableFact" package instead.
            Skip.If(String.IsNullOrWhiteSpace(connstr), "Test disabled - No connection string provided for test database.");

            RepositoryType repoType = (RepositoryType)Enum.Parse(typeof(RepositoryType), repoTypeString);
            string expectedTableName = configuredTableName ?? GetDefaultTableName(repoType);

            // Ensure the database does not yet contain default schema
            await VerifyDatabaseSchema(connstr, expectedTableName, GetSProcNames(repoType, configuredTableName), doesNotExist: true);

            // Also ensure the database does not yet contain custom-table schema when using a custom table
            if (configuredTableName != null)
                await VerifyDatabaseSchema(connstr, GetDefaultTableName(repoType), GetSProcNames(repoType, null), doesNotExist: true);

            try
            {
                // Initialize the provider - No exceptions
                var provider = CreateProvider();
                provider.Initialize(DefaultProviderName, CreateSessionStateProviderConfig(repoType: repoType.ToString(), tableName: configuredTableName),
                    CreateSessionStateSection(timeoutInSecond: 42, compressionEnabled: true),
                    CreateConnectionStringSettings(connstr),
                    shouldCreateTable: true);

                // Verify only the expected schema/sprocs are created
                (int initialTableCount, int initialSProcCount) = await VerifyDatabaseSchema(connstr, expectedTableName, GetSProcNames(repoType, configuredTableName));
                if (configuredTableName != null)
                    await VerifyDatabaseSchema(connstr, GetDefaultTableName(repoType), GetSProcNames(repoType, null), doesNotExist: true);


                // Initialize another provider - verify no error
                provider = CreateProvider();
                provider.Initialize(DefaultProviderName, CreateSessionStateProviderConfig(repoType: repoType.ToString(), tableName: configuredTableName),
                    CreateSessionStateSection(timeoutInSecond: 42, compressionEnabled: true),
                    CreateConnectionStringSettings(connstr),
                    shouldCreateTable: true);

                // Verify the expected schema/sprocs are still created and no more
                (int currentTableCount, int currentSProcCount) = await VerifyDatabaseSchema(connstr, expectedTableName, GetSProcNames(repoType, configuredTableName));
                if (configuredTableName != null)
                    await VerifyDatabaseSchema(connstr, GetDefaultTableName(repoType), GetSProcNames(repoType, null), doesNotExist: true);
                Assert.Equal(initialTableCount, currentTableCount);
                Assert.Equal(initialSProcCount, currentSProcCount);
            }
            finally
            {
                if (repoType != RepositoryType.FrameworkCompat)
                {
                    // We started with a clean slate. (Verified before entering try/finally.)
                    // Drop the objects we added to leave a clean state for future tests.
                    using (var sqlConn = new SqlConnection(connstr))
                    {
                        string query = "";
                        foreach (var spName in GetSProcNames(repoType, configuredTableName))
                            query += $"DROP PROCEDURE IF EXISTS [{spName}];";
                        query += $"DROP TABLE IF EXISTS [{expectedTableName}];";

                        if (configuredTableName != null)
                        {
                            foreach (var spName in GetSProcNames(repoType, null))
                                query += $"DROP PROCEDURE IF EXISTS [{spName}];";
                            query += $"DROP TABLE IF EXISTS [{GetDefaultTableName(repoType)}];";
                        }

                        var sqlCmd = new SqlCommand(query, sqlConn);
                        await sqlConn.OpenAsync();
                        await sqlCmd.ExecuteNonQueryAsync();
                    }
                }
            }
        }

        [SkippableTheory]
        [InlineData(sqlServerConnStr, null, "Fx7")]
        [InlineData(sqlServerConnStr, null, "Fx8")]
        [InlineData(sqlServerConnStr, null, "Async11")]
        [InlineData(sqlServerConnStr, null, "Async20")]
        public async void CompatMode_Detects_Async11(string connstr, string configuredTableName, string oldTableType)
        {
            // Skip if no connection string.
            // Assert.Skip will work in XUnit V3, but the latest release is only 2.4.2. Use the 3rd party "Xunit.SkippableFact" package instead.
            Skip.If(String.IsNullOrWhiteSpace(connstr), "Test disabled - No connection string provided for test database.");

            var repoType = RepositoryType.FrameworkCompat;
            string expectedTableName = configuredTableName ?? GetDefaultTableName(repoType);

            using (var sqlConn = new SqlConnection(connstr))
            {
                // Ensure the database does not contain the session table
                await VerifyDatabaseSchema(connstr, expectedTableName, new string[] { }, doesNotExist: true);

                try
                {
                    // TODO - Setting up a fake old-style DB should be a refactored utility for easy re-use
                    // For this testcase, we don't need stored procs. Just a skeleton table.
                    string createTableSql = @"CREATE TABLE " + expectedTableName + @" (
                        SessionId           nvarchar(88)    NOT NULL PRIMARY KEY,
                        Created             datetime        NOT NULL DEFAULT GETUTCDATE(),
                        Expires             datetime        NOT NULL,
                        LockDate            datetime        NOT NULL,
                        " + (((oldTableType != "Fx7")) ? "LockDateLocal       datetime        NOT NULL," : "") + @"
                        LockCookie          int             NOT NULL,
                        Timeout             int             NOT NULL,
                        Locked              bit             NOT NULL,
                        " + ((oldTableType.StartsWith("Fx")) ? "SessionItemShort    VARBINARY(7000) NULL," : "") + @"
                        SessionItemLong     " + ((oldTableType == "Async20") ? "varbinary(max)" : "image") + @"           NULL,
                        Flags               int             NOT NULL DEFAULT 0,
                        )";
                    var cmd = new SqlCommand(createTableSql, sqlConn);
                    await sqlConn.OpenAsync();
                    await cmd.ExecuteNonQueryAsync();

                    // Ensure the database does contain default table now
                    (int initialTableCount, int initialSProcCount) = await VerifyDatabaseSchema(connstr, expectedTableName, new string[] { });

                    // Initialize the provider - Mostly no exceptions
                    var provider = CreateProvider();
                    if (oldTableType != "Async20")
                    {
                        provider.Initialize(DefaultProviderName, CreateSessionStateProviderConfig(repoType: repoType.ToString(), tableName: configuredTableName),
                            CreateSessionStateSection(timeoutInSecond: 43, compressionEnabled: true),
                            CreateConnectionStringSettings(connstr),
                            shouldCreateTable: true);

                        Assert.IsType<SqlFxCompatSessionStateRepository>(provider.SqlSessionStateRepository);
                        var repo = provider.SqlSessionStateRepository as SqlFxCompatSessionStateRepository;
                        Assert.Equal(oldTableType, repo.SessionTableTypeString);

                        // Verify our provider did not add any new tables or sprocs
                        (int currentTableCount, int currentSProcCount) = await VerifyDatabaseSchema(connstr, expectedTableName, new string[] { });
                        Assert.Equal(initialTableCount, currentTableCount);
                        Assert.Equal(initialSProcCount, currentSProcCount);
                    }
                    else
                    {
                        var ex = Record.Exception(() => provider.Initialize(DefaultProviderName, CreateSessionStateProviderConfig(repoType: repoType.ToString(), tableName: configuredTableName),
                            CreateSessionStateSection(timeoutInSecond: 43, compressionEnabled: true),
                            CreateConnectionStringSettings(connstr),
                            shouldCreateTable: true));

                        Assert.NotNull(ex);
                        Assert.IsType<HttpException>(ex);
                        Assert.NotNull(ex.InnerException);
                        Assert.Equal($"The table '{expectedTableName}' is compatible with current repositories. Use the 'SqlServer' repositoryType instead.", ex.InnerException.Message);
                    }
                }
                finally
                {
                    // We started with a clean slate. (Verified before entering try/finally.)
                    // Drop the objects we added to leave a clean state for future tests.
                    var cmd = new SqlCommand("DROP TABLE " + expectedTableName, sqlConn);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }
#endif

        private async Task<(int tableCount, int sprocCount)> VerifyDatabaseSchema(string connstr, string tableName, string[] sprocNames, bool doesNotExist = false)
        {
            using (var sqlConn = new SqlConnection(connstr))
            {
                string query = $@"
                    DECLARE @totalTables int
                    SELECT @totalTables = Count(*) FROM INFORMATION_SCHEMA.TABLES

                    DECLARE @totalSProcs int
                    SELECT @totalSProcs = Count(*) FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE = 'PROCEDURE'

                    DECLARE @foundTable int
                    SET @foundTable=0
                    IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '{tableName}')
                        SET @foundTable=1

                    DECLARE @foundSProcCount int
                    SET @foundSProcCount=0
                ";

                foreach (var sprocName in sprocNames)
                {
                    query += $@"
                        IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE = 'PROCEDURE' AND ROUTINE_NAME = '{sprocName}')
                            SET @foundSProcCount=@foundSProcCount+1
                    ";
                }

                query += "SELECT @totalTables, @totalSProcs, @foundTable, @foundSProcCount;";

                var sqlCmd = new SqlCommand(query, sqlConn);
                await sqlConn.OpenAsync();
                var reader = await sqlCmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    int tableCount = await reader.GetFieldValueAsync<int>(0);
                    int sprocCount = await reader.GetFieldValueAsync<int>(1);
                    int foundTable = await reader.GetFieldValueAsync<int>(2);
                    int foundSProcs = await reader.GetFieldValueAsync<int>(3);

                    if (!doesNotExist) // Expect to find all these tables/sprocs do in fact exist
                    {
                        Assert.Equal(1, foundTable);
                        Assert.Equal(sprocNames.Length, foundSProcs);
                    }
                    else // Do not expect to find any of these tables/sprocs
                    {
                        Assert.Equal(0, foundTable);
                        Assert.Equal(0, foundSProcs);
                    }

                    return (tableCount, sprocCount);
                }
            }

            return (-1, -1);
        }

        private string[] GetSProcNames(RepositoryType repoType, string tableName)
        {
            var post = "";
            if (string.IsNullOrEmpty(tableName))
            {
                post = (repoType == RepositoryType.InMemory) ? "M" : (repoType == RepositoryType.InMemoryDurable) ? "MD" : "";
            }

            switch (repoType)
            {
                case RepositoryType.SqlServer:
                case RepositoryType.InMemory:
                case RepositoryType.InMemoryDurable:
                    string pre = string.IsNullOrEmpty(tableName) ? "" : $"{tableName}_";
                    return new string[]
                    {
                        pre + "GetStateItemExclusive" + post,
                        pre + "GetStateItem" + post,
                        pre + "DeleteExpiredSessionState" + post,
                        pre + "InsertStateItem" + post,
                        pre + "InsertUninitializedItem" + post,
                        pre + "ReleaseItemExclusive" + post,
                        pre + "RemoveStateItem" + post,
                        pre + "ResetItemTimeout" + post,
                        pre + "UpdateStateItem" + post,
                    };
            }

            return new string[] { };
        }

        // ==================================================================================================================================================
        //
        // Helper methods
        //
        //

        private string GetDefaultTableName(RepositoryType repoType)
        {
            switch (repoType)
            {
                case RepositoryType.SqlServer:
                    return "ASPNetSessionState";
                case RepositoryType.InMemory:
                    return "ASPNetSessionState_InMem";
                case RepositoryType.InMemoryDurable:
                    return "ASPNetSessionState_InMemDurable";
                case RepositoryType.FrameworkCompat:
                    return "ASPStateTempSessions";
            }

            return null;
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

        private NameValueCollection CreateSessionStateProviderConfig(string useInMemoryTable = null, string retryInterval = null, string maxRetryNum = null, string repoType = null, string tableName = null)
        {
            var config = new NameValueCollection();

            config[InMemoryTableConfigurationName] = useInMemoryTable;
            config[MaxRetryConfigurationName] = maxRetryNum;
            config[RetryIntervalConfigurationName] = retryInterval;
            config[RepositoryTypeConfigurationName] = repoType;
            config[TableNameConfigurationName] = tableName;

            return config;
        }

        private ConnectionStringSettings CreateConnectionStringSettings(string connectStr = DefaultTestConnectionString)
        {
            var connectionStrSetting = new ConnectionStringSettings();
            connectionStrSetting.ConnectionString = connectStr;

            return connectionStrSetting;
        }
    }
}
