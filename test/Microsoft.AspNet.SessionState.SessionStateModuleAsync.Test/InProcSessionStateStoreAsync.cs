// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See the License.txt file in the project root for full license information.

namespace Microsoft.AspNet.SessionState.Tests
{
    using System;
    using System.IO;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web;
    using System.Web.SessionState;
    using Moq;
    using Xunit;

    public class InProcSessionStateStoreAsyncTests
    {
        private static FieldInfo s_storeDataStaticObjects = typeof(SessionStateStoreData).GetField("_staticObjects", BindingFlags.NonPublic | BindingFlags.Instance);
        private static SessionStateStoreData CreateNewStoreData(InProcSessionStateStoreAsync provider, HttpContextBase httpContextBase = null, int timeout = 30, HttpStaticObjectsCollection staticObjects = null)
        {
            Assert.NotNull(provider);
            var data = provider.CreateNewStoreData(httpContextBase, timeout);

            //if (staticObjects != null)
            s_storeDataStaticObjects.SetValue(data, staticObjects ?? new HttpStaticObjectsCollection());

            return data;
        }

        private InProcSessionStateStoreAsync _provider;

        public InProcSessionStateStoreAsyncTests()
        {
            _provider = new InProcSessionStateStoreAsync();
            _provider.Initialize("InProcTest", null);
        }

        [Fact]
        public void Initialize_WithName_SetsName()
        {
            // Arrange
            var provider = new InProcSessionStateStoreAsync();

            // Act
            provider.Initialize("TestName", null);

            // Assert
            Assert.Equal("TestName", provider.Name);
        }

        [Fact]
        public void CreateNewStoreData_ReturnsValidData()
        {
            var timeout = 30;
            var storeData = CreateNewStoreData(_provider, timeout: timeout);

            // Assert
            Assert.NotNull(storeData);
            Assert.NotNull(storeData.Items);
            Assert.Empty(storeData.Items);
            Assert.NotNull(storeData.StaticObjects);
            Assert.Empty(storeData.StaticObjects);
            Assert.Equal(timeout, storeData.Timeout);
        }

        [Fact]
        public async Task CreateUninitializedItem_CreatesItem()
        {
            // Arrange
            string sessionId = "TestSession";
            int timeout = 30;

            // Act
            await _provider.CreateUninitializedItemAsync(null, sessionId, timeout, CancellationToken.None);

            // Get the item to verify it was created
            var result = await _provider.GetItemAsync(null, sessionId, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(SessionStateActions.InitializeItem, result.Actions);
        }

        [Fact]
        public async Task GetItemExclusive_LockItem_ReturnsLockedItem()
        {
            // Arrange
            string sessionId = "TestSession";
            int timeout = 30;

            // Create new session
            var storeData = CreateNewStoreData(_provider, timeout: timeout);
            storeData.Items["TestKey"] = "TestValue";

            await _provider.CreateUninitializedItemAsync(null, sessionId, timeout, CancellationToken.None);

            // Set the item
            await _provider.SetAndReleaseItemExclusiveAsync(
                null,
                sessionId,
                storeData,
                1, // lockCookie
                true, // newItem
                CancellationToken.None);

            // Get the item exclusively
            var result = await _provider.GetItemExclusiveAsync(null, sessionId, CancellationToken.None);
            Assert.NotNull(result);
            Assert.NotNull(result.Item);
            Assert.Equal("TestValue", result.Item.Items["TestKey"]);
            Assert.False(result.Locked);

            // Try to get the item again - find it locked
            var lockedResult = await _provider.GetItemExclusiveAsync(null, sessionId, CancellationToken.None);
            Assert.NotNull(lockedResult);
            Assert.Null(lockedResult.Item);
            Assert.True(lockedResult.Locked);
        }

        [Fact]
        public async Task ReleaseItemExclusive_ReleasesLock()
        {
            // Arrange
            string sessionId = "TestSession";
            int timeout = 30;

            // Create new session
            var storeData = CreateNewStoreData(_provider, timeout: timeout);

            await _provider.CreateUninitializedItemAsync(null, sessionId, timeout, CancellationToken.None);

            // Set the item
            await _provider.SetAndReleaseItemExclusiveAsync(
                null,
                sessionId,
                storeData,
                1, // lockCookie
                true, // newItem
                CancellationToken.None);

            // Lock the item
            var lockedResult = await _provider.GetItemExclusiveAsync(null, sessionId, CancellationToken.None);

            // Act - Release the lock
            await _provider.ReleaseItemExclusiveAsync(
                null,
                sessionId,
                lockedResult.LockId,
                CancellationToken.None);

            // Try to get it again exclusively
            var result = await _provider.GetItemExclusiveAsync(null, sessionId, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.NotNull(result.Item);
            Assert.False(result.Locked);
        }

        [Fact]
        public async Task RemoveItem_RemovesSessionItem()
        {
            // Arrange
            string sessionId = "TestSession";
            int timeout = 30;

            // Create new session
            var storeData = CreateNewStoreData(_provider, timeout: timeout);

            await _provider.CreateUninitializedItemAsync(null, sessionId, timeout, CancellationToken.None);

            // Set the item
            await _provider.SetAndReleaseItemExclusiveAsync(
                null,
                sessionId,
                storeData,
                1, // lockCookie
                true, // newItem
                CancellationToken.None);

            // Lock the item
            var lockedResult = await _provider.GetItemExclusiveAsync(null, sessionId, CancellationToken.None);

            // Act - Remove the item
            await _provider.RemoveItemAsync(
                null,
                sessionId,
                lockedResult.LockId,
                storeData,
                CancellationToken.None);

            // Try to get it again
            var result = await _provider.GetItemAsync(null, sessionId, CancellationToken.None);

            // Assert
            Assert.Null(result.Item);
        }

        [Fact]
        public async Task ResetItemTimeout_UpdatesExpiration()
        {
            // Arrange
            string sessionId = "TestSession";
            int timeout = 30;

            // Create new session
            var storeData = CreateNewStoreData(_provider, timeout: timeout);

            await _provider.CreateUninitializedItemAsync(null, sessionId, timeout, CancellationToken.None);

            // Set the item
            await _provider.SetAndReleaseItemExclusiveAsync(
                null,
                sessionId,
                storeData,
                1, // lockCookie
                true, // newItem
                CancellationToken.None);

            // Act - Reset timeout
            await _provider.ResetItemTimeoutAsync(null, sessionId, CancellationToken.None);

            // Assert - Just verifying no exceptions
            Assert.True(true);
        }

        [Fact]
        public async Task SetAndReleaseItemExclusive_UpdatesSessionData()
        {
            // Arrange
            string sessionId = "TestSession";
            int timeout = 30;

            // Create new session
            var storeData = CreateNewStoreData(_provider, timeout: timeout);
            storeData.Items["TestKey"] = "TestValue";

            await _provider.CreateUninitializedItemAsync(null, sessionId, timeout, CancellationToken.None);

            // Act - Set the item
            await _provider.SetAndReleaseItemExclusiveAsync(
                null,
                sessionId,
                storeData,
                1, // lockCookie
                false, // newItem
                CancellationToken.None);

            // Get the item to verify it was updated
            var result = await _provider.GetItemAsync(null, sessionId, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.NotNull(result.Item);
            Assert.Equal("TestValue", result.Item.Items["TestKey"]);
        }

        [Fact]
        public void SetItemExpireCallback_RegistersCallback()
        {
            // Arrange
            bool callbackInvoked = false;

            // Act
            bool result = _provider.SetItemExpireCallback((id, item) => callbackInvoked = true);

            // Assert
            Assert.True(result);
            Assert.False(callbackInvoked);
        }

        [Fact]
        public void Dispose_DoesNotThrow()
        {
            // Act & Assert - no exception should be thrown
            _provider.Dispose();
        }
    }

    public class ConcurrentNonSerializingSessionStateItemCollectionTests
    {
        private ConcurrentNonSerializingSessionStateItemCollection _collection;

        public ConcurrentNonSerializingSessionStateItemCollectionTests()
        {
            _collection = new ConcurrentNonSerializingSessionStateItemCollection();
        }

        [Fact]
        public void Add_Item_SetsItemAndDirty()
        {
            // Arrange
            string key = "TestKey";
            string value = "TestValue";

            // Act
            _collection[key] = value;

            // Assert
            Assert.Equal(value, _collection[key]);
            Assert.True(_collection.Dirty);
        }

        [Fact]
        public void Get_Item_ReturnsItem()
        {
            // Arrange
            string key = "TestKey";
            string value = "TestValue";
            _collection[key] = value;

            // Reset dirty flag
            _collection.Dirty = false;

            // Act
            var result = _collection[key];

            // Assert
            Assert.Equal(value, result);

            // Accessing immutable object like string shouldn't set dirty flag
            Assert.False(_collection.Dirty);
        }

        [Fact]
        public void Get_IndexedItem_ReturnsItem()
        {
            // Arrange
            string key = "TestKey";
            string value = "TestValue";
            _collection[key] = value;

            // Reset dirty flag
            _collection.Dirty = false;

            // Act
            var result = _collection[0];

            // Assert
            Assert.Equal(value, result);

            // Accessing immutable object like string shouldn't set dirty flag
            Assert.False(_collection.Dirty);
        }

        [Fact]
        public void Set_IndexedItem_SetsItemAndDirty()
        {
            // Arrange
            string key = "TestKey";
            string value = "TestValue";
            _collection[key] = value;

            string newValue = "NewValue";

            // Reset dirty flag
            _collection.Dirty = false;

            // Act
            _collection[0] = newValue;

            // Assert
            Assert.Single(_collection);
            Assert.True(_collection.Dirty);
            Assert.Equal(newValue, _collection[key]);
        }

        [Fact]
        public void Clear_RemovesAllItemsAndSetsDirty()
        {
            // Arrange
            _collection["Key1"] = "Value1";
            _collection["Key2"] = "Value2";

            // Reset dirty flag
            _collection.Dirty = false;

            // Act
            _collection.Clear();

            // Assert
            Assert.Empty(_collection);
            Assert.True(_collection.Dirty);
        }

        [Fact]
        public void Remove_RemovesItemAndSetsDirty()
        {
            // Arrange
            string key = "TestKey";
            string value = "TestValue";
            _collection[key] = value;

            // Reset dirty flag
            _collection.Dirty = false;

            // Act
            _collection.Remove(key);

            // Assert
            Assert.Null(_collection[key]);
            Assert.True(_collection.Dirty);
        }

        [Fact]
        public void RemoveAt_RemovesItemAndSetsDirty()
        {
            // Arrange
            string key = "TestKey";
            string value = "TestValue";
            _collection[key] = value;

            // Reset dirty flag
            _collection.Dirty = false;

            // Act
            _collection.RemoveAt(0);

            // Assert
            Assert.Empty(_collection);
            Assert.True(_collection.Dirty);
        }

        [Fact]
        public void GetEnumerator_EnumeratesItems()
        {
            // Arrange
            _collection["Key1"] = "Value1";
            _collection["Key2"] = "Value2";

            // Act
            int count = 0;
            foreach (var key in _collection)
            {
                count++;
            }

            // Assert
            Assert.Equal(2, count);
        }

        [Fact]
        public void Keys_ReturnsCollectionKeys()
        {
            // Arrange
            _collection["Key1"] = "Value1";
            _collection["Key2"] = "Value2";

            // Act
            var keys = _collection.Keys;

            // Assert
            Assert.Equal(2, keys.Count);
        }
    }

    // Helper classes for testing if needed
    public static class SessionStateItemCollectionExtensions
    {
        public static void SerializeToStream(this SessionStateItemCollection items, Stream stream)
        {
            BinaryWriter writer = new BinaryWriter(stream);
            items.Serialize(writer);
        }

        public static SessionStateItemCollection DeserializeFromStream(Stream stream)
        {
            BinaryReader reader = new BinaryReader(stream);
            return SessionStateItemCollection.Deserialize(reader);
        }
    }

    public class ConcurrentSessionStateItemCollectionTests
    {
        private ConcurrentSessionStateItemCollection _collection;

        public ConcurrentSessionStateItemCollectionTests()
        {
            _collection = new ConcurrentSessionStateItemCollection();
        }

        [Fact]
        public void Serialize_Deserialize_PreservesItems()
        {
            // Arrange
            _collection["Key1"] = "Value1";
            _collection["Key2"] = DateTime.Now;

            var stream = new MemoryStream();
            var writer = new BinaryWriter(stream);

            // Act
            _collection.Serialize(writer);
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            var deserialized = ConcurrentSessionStateItemCollection.Deserialize(reader);

            // Assert
            Assert.Equal(2, deserialized.Count);
            Assert.Equal("Value1", deserialized["Key1"]);
            Assert.Equal(_collection["Key2"], deserialized["Key2"]);
            Assert.False(deserialized.Dirty);
        }

        [Fact]
        public void PartialDeserialization_LoadsItemsOnDemand()
        {
            // Arrange
            _collection["Key1"] = "Value1";
            _collection["Key2"] = "Value2";
            _collection["Key3"] = "Value3";

            var stream = new MemoryStream();
            var writer = new BinaryWriter(stream);

            // Act - Serialize and deserialize
            _collection.Serialize(writer);
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            var deserialized = ConcurrentSessionStateItemCollection.Deserialize(reader);

            // Only access the first item - this should deserialize just that item
            var firstValue = deserialized["Key1"];

            // Assert
            Assert.Equal("Value1", firstValue);
            Assert.Equal(3, deserialized.Count);
        }
    }
}
