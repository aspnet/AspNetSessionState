// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See the License.txt file in the project root for full license information.

namespace Microsoft.AspNet.SessionState.CosmosDBSessionStateAsyncProvider.Test
{
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System;
    using System.Web.SessionState;
    using Xunit;

    public class SessionStateItemTest
    {
        private const string TestSessionId = "piqhlifa30ooedcp1k42mtef";
        private const string JsonTemplate = "{{\"id\":\"{0}\",\"lockAge\":{1},\"lockCookie\":{2},\"ttl\":{3},\"locked\":{4},\"sessionItem\":\"{5}\",\"uninitialized\":{6}}}";

        [Fact]
        public void SessionStateItem_Can_Be_Serialized_With_Null_LockAge()
        {
            var item = new SessionStateItem()
            {
                Actions = SessionStateActions.None,
                LockAge = null,
                LockCookie = 1,
                Locked = false,
                SessionId = TestSessionId,
                SessionItem = new byte[2] { 1, 1 },
                Timeout = 10
            };
            var json = JsonSerializer.Serialize<SessionStateItem>(item);
            var expected = string.Format(JsonTemplate, item.SessionId, "null", item.LockCookie, item.Timeout, item.Locked, "AQE=", false); 

            Assert.Equal(expected, json, true);
        }
        
        [Fact]
        public void SessionStateItem_Can_Be_Serialized_With_LockAge()
        {
            var item = new SessionStateItem()
            {
                Actions = SessionStateActions.InitializeItem,
                LockAge = TimeSpan.FromMinutes(1),
                LockCookie = 1,
                Locked = true,
                SessionId = TestSessionId,
                SessionItem = new byte[2] { 1, 1 },
                Timeout = 10
            };
            var json = JsonSerializer.Serialize(item);
            var expected = string.Format(JsonTemplate, item.SessionId, 60 * 1, item.LockCookie, item.Timeout, item.Locked, "AQE=", true);

            Assert.Equal(expected, json, true);
        }

        [Fact]
        public void SessionStateItem_Can_Be_Deserialized_With_Null_Uninitialized()
        {
            var json = string.Format(JsonTemplate, TestSessionId, 60, 1, 20, "false", "AQE=", "null");

            var item = JsonSerializer.Deserialize<SessionStateItem>(json);

            Assert.Equal(TestSessionId, item.SessionId);
            Assert.False(item.Actions.HasValue);
            Assert.Equal(1, item.LockCookie);
            Assert.Equal(new byte[2] { 1, 1 }, item.SessionItem);
            Assert.False(item.Locked);
            Assert.Equal(20, item.Timeout);
            Assert.Equal(TimeSpan.FromSeconds(60), item.LockAge);
        }

        [Fact]
        public void SessionStateItem_Can_Be_Deserialized_With_True_Uninitialized()
        {
            var json = string.Format(JsonTemplate, TestSessionId, 60, 1, 20, "false", "AQE=", "true");

            var item = JsonSerializer.Deserialize<SessionStateItem>(json);

            Assert.Equal(TestSessionId, item.SessionId);
            Assert.Equal(SessionStateActions.InitializeItem, item.Actions);
            Assert.Equal(1, item.LockCookie);
            Assert.Equal(new byte[2] { 1, 1 }, item.SessionItem);
            Assert.False(item.Locked);
            Assert.Equal(20, item.Timeout);
            Assert.Equal(TimeSpan.FromSeconds(60), item.LockAge);
        }

        [Fact]
        public void SessionStateItem_Can_Be_Deserialized_With_False_Uninitialized_And_Null_Lockage()
        {
            var json = string.Format(JsonTemplate, TestSessionId, "null", 1, 10, "true", "AQE=", "false");

            var item = JsonSerializer.Deserialize<SessionStateItem>(json);

            Assert.Equal(TestSessionId, item.SessionId);
            Assert.Equal(SessionStateActions.None, item.Actions);
            Assert.Equal(1, item.LockCookie);
            Assert.Equal(new byte[2] { 1, 1 }, item.SessionItem);
            Assert.True(item.Locked);
            Assert.Equal(10, item.Timeout);
            Assert.False(item.LockAge.HasValue);
        }
    }
}
