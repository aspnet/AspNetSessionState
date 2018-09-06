using System;
using System.Collections.Specialized;
using System.Configuration;
using System.IO;
using System.Web;
using System.Web.Configuration;
using System.Web.SessionState;

using Moq;

using Xunit;

namespace Microsoft.AspNet.SessionState.RedisSessionStateAsyncProvider.Test
{
    public class RedisSessionStateProviderAsyncTest
    {
        private const string DatabaseIdConfigurationName = "databaseId";
        private const int DefaultSessionTimeout = 20;
        private const string DefaultTestConnectionString = "";
        private const string DefaultProviderName = "testprovider";

        public RedisSessionStateProviderAsyncTest()
        {
            RedisSessionStateProviderAsync.GetSessionStaticObjects = cxt => new HttpStaticObjectsCollection();
        }

        [Fact]
        public void Initialize_With_DefaultConfig_Should_Use_SqlSessionStateRepository_With_DefaultSettings()
        {
            var provider = CreateProvider();
            provider.Initialize(DefaultProviderName, CreateSessionStateProviderConfig(), CreateSessionStateSection(),
                CreateConnectionStringSettings());

            Assert.Equal(DefaultProviderName, provider.Name);
            Assert.False(provider.CompressionEnabled);
            
            Assert.Equal(DefaultTestConnectionString, provider.ConnectionString);
            Assert.Equal(-1, provider.DatabaseId);
        }

        [Fact]
        public void Initialize_With_SqlSessionStateRepositorySettings_Should_Use_Configured_Settings()
        {
            var provider = CreateProvider();
            provider.Initialize(DefaultProviderName, CreateSessionStateProviderConfig("1"),
                CreateSessionStateSection(timeoutInSecond: 88, compressionEnabled: true),
                CreateConnectionStringSettings());

            Assert.Equal(DefaultProviderName, provider.Name);
            Assert.True(provider.CompressionEnabled);

            Assert.Equal(DefaultTestConnectionString, provider.ConnectionString);
            Assert.Equal(1, provider.DatabaseId);
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

            byte[] buff;
            SessionStateStoreData deserializedData;

            RedisSessionStateProviderAsync.SerializeStoreData(data, out buff, enableCompression);
            using (var stream = new MemoryStream(buff))
            {
                var httpContext = CreateMoqHttpContextBase();
                deserializedData = RedisSessionStateProviderAsync.DeserializeStoreData(httpContext, stream, enableCompression);
            }

            Assert.Equal(data.Items.Count, deserializedData.Items.Count);
            Assert.Equal(data.Items["test1"], deserializedData.Items["test1"]);
            Assert.Equal(now, (DateTime)deserializedData.Items["test2"]);
            Assert.NotNull(deserializedData.StaticObjects);
            Assert.Equal(data.Timeout, deserializedData.Timeout);
        }

        private RedisSessionStateProviderAsync CreateProvider()
        {
            var provider = new RedisSessionStateProviderAsync();
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

        private NameValueCollection CreateSessionStateProviderConfig(string databaseId = "-1")
        {
            var config = new NameValueCollection();

            config[DatabaseIdConfigurationName] = databaseId;

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
