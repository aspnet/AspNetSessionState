using Microsoft.AspNet.SessionState.Resources;
using System;
using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Web;
using HttpRuntime = System.Web.HttpRuntime;
using ISessionStateItemCollection = System.Web.SessionState.ISessionStateItemCollection;

namespace Microsoft.AspNet.SessionState
{
    /// <summary>
    /// A threadsafe collection of objects stored in session state. Does not serialize state. This class cannot be inherited.
    /// </summary>
    public sealed class ConcurrentNonSerializingSessionStateItemCollection : NameObjectCollectionBase, ISessionStateItemCollection, ICollection, IEnumerable
    {
        // It should be noted that the base NameObjectCollectionBase isn't really intrinsically threadsafe.
        // We protect the basic operations with a lock here, but we don't protect GetEnumerator/Keys
        // scenarios. It is incumbant upon the caller to ensure that they don't modify the collection while
        // enumerating it. This is a limitation of the NameObjectCollectionBase class.
        private object _collectionLock = new object();

        /// <summary>Gets or sets a value indicating whether the collection has been marked as changed.</summary>
        /// <returns>true if the <see cref="T:System.Web.SessionState.SessionStateItemCollection" /> contents have been changed; otherwise, false.</returns>
        public bool Dirty { get; set; }

        /// <summary>Gets or sets a value in the collection by name.</summary>
        /// <returns>The value in the collection with the specified name. If the specified key is not found, attempting to get it returns null, and attempting to set it creates a new element using the specified key.</returns>
        /// <param name="name">The key name of the value in the collection.</param>
        public object this[string name]
        {
            get
            {
                lock (this._collectionLock)
                {
                    object obj = base.BaseGet(name);
                    if (obj != null && !IsImmutable(obj))
                    {
                        this.Dirty = true;
                    }
                    return obj;
                }
            }
            set
            {
                lock (this._collectionLock)
                {
                    base.BaseSet(name, value);
                    this.Dirty = true;
                }
            }
        }

        /// <summary>Gets or sets a value in the collection by numerical index.</summary>
        /// <returns>The value in the collection stored at the specified index. If the specified key is not found, attempting to get it returns null, and attempting to set it creates a new element using the specified key.</returns>
        /// <param name="index">The numerical index of the value in the collection.</param>
        public object this[int index]
        {
            get
            {
                lock (this._collectionLock)
                {
                    object obj = base.BaseGet(index);
                    if (obj != null && !IsImmutable(obj))
                    {
                        this.Dirty = true;
                    }
                    return obj;
                }
            }
            set
            {
                lock (this._collectionLock)
                {
                    base.BaseSet(index, value);
                    this.Dirty = true;
                }
            }
        }

        /// <summary>Gets a collection of the variable names for all values stored in the collection.</summary>
        /// <returns>The <see cref="T:System.Collections.Specialized.NameObjectCollectionBase.KeysCollection" /> collection that contains all the collection keys. </returns>
        public override NameObjectCollectionBase.KeysCollection Keys
        {
            get
            {
                return base.Keys;
            }
        }

        /// <summary>Creates a new, empty <see cref="T:System.Web.SessionState.SessionStateItemCollection" /> object.</summary>
        public ConcurrentNonSerializingSessionStateItemCollection() : base(Misc.CaseInsensitiveInvariantKeyComparer)
        {
        }

        /// <summary>Removes all values and keys from the session-state collection.</summary>
        public void Clear()
        {
            lock (this._collectionLock)
            {
                base.BaseClear();
                this.Dirty = true;
            }
        }

        /// <summary>Returns an enumerator that can be used to read all the key names in the collection.</summary>
        /// <returns>An <see cref="T:System.Collections.IEnumerator" /> that can iterate through the variable names in the session-state collection.</returns>
        public override IEnumerator GetEnumerator()
        {
            return base.GetEnumerator();
        }

        internal static bool IsImmutable(object o)
        {
            return Misc.ImmutableTypes[o.GetType()] != null;
        }

        /// <summary>Deletes an item from the collection.</summary>
        /// <param name="name">The name of the item to delete from the collection. </param>
        public void Remove(string name)
        {
            lock (this._collectionLock)
            {
                base.BaseRemove(name);
                this.Dirty = true;
            }
        }

        /// <summary>Deletes an item at a specified index from the collection.</summary>
        /// <param name="index">The index of the item to remove from the collection. </param>
        /// <exception cref="T:System.ArgumentOutOfRangeException">
        ///   <paramref name="index" /> is less than zero.- or -<paramref name="index" /> is equal to or greater than <see cref="P:System.Collections.ICollection.Count" />.</exception>
        public void RemoveAt(int index)
        {
            lock (this._collectionLock)
            {
                base.BaseRemoveAt(index);
                this.Dirty = true;
            }
        }
    }

    /// <summary>A thread-safe collection of objects stored in session state. This class cannot be inherited.</summary>
    public sealed class ConcurrentSessionStateItemCollection : NameObjectCollectionBase, ISessionStateItemCollection, ICollection, IEnumerable
    {
        private const int NO_NULL_KEY = -1;
        private const int SIZE_OF_INT32 = 4;

        private bool _dirty;
        private KeyedCollection _serializedItems;
        private Stream _stream;
        private int _iLastOffset;
        private object _serializedItemsLock = new object();

        /// <summary>Gets or sets a value indicating whether the collection has been marked as changed.</summary>
        /// <returns>true if the <see cref="T:System.Web.SessionState.SessionStateItemCollection" /> contents have been changed; otherwise, false.</returns>
        public bool Dirty
        {
            get
            {
                return this._dirty;
            }
            set
            {
                this._dirty = value;
            }
        }

        /// <summary>Gets or sets a value in the collection by name.</summary>
        /// <returns>The value in the collection with the specified name. If the specified key is not found, attempting to get it returns null, and attempting to set it creates a new element using the specified key.</returns>
        /// <param name="name">The key name of the value in the collection.</param>
        public object this[string name]
        {
            get
            {
                lock (this._serializedItemsLock)
                {
                    this.DeserializeItem(name, true);
                    object obj = base.BaseGet(name);
                    if (obj != null && !IsImmutable(obj))
                    {
                        this._dirty = true;
                    }
                    return obj;
                }
            }
            set
            {
                lock (this._serializedItemsLock)
                {
                    this.MarkItemDeserialized(name);
                    base.BaseSet(name, value);
                    this._dirty = true;
                }
            }
        }

        /// <summary>Gets or sets a value in the collection by numerical index.</summary>
        /// <returns>The value in the collection stored at the specified index. If the specified key is not found, attempting to get it returns null, and attempting to set it creates a new element using the specified key.</returns>
        /// <param name="index">The numerical index of the value in the collection.</param>
        public object this[int index]
        {
            get
            {
                lock (this._serializedItemsLock)
                {
                    this.DeserializeItem(index);
                    object obj = base.BaseGet(index);
                    if (obj != null && !IsImmutable(obj))
                    {
                        this._dirty = true;
                    }
                    return obj;
                }
            }
            set
            {
                lock (this._serializedItemsLock)
                {
                    this.MarkItemDeserialized(index);
                    base.BaseSet(index, value);
                    this._dirty = true;
                }
            }
        }

        /// <summary>Gets a collection of the variable names for all values stored in the collection.</summary>
        /// <returns>The <see cref="T:System.Collections.Specialized.NameObjectCollectionBase.KeysCollection" /> collection that contains all the collection keys. </returns>
        public override NameObjectCollectionBase.KeysCollection Keys
        {
            get
            {
                // Unfortunately, we have to deserialize all items first, because Keys.GetEnumerator might
                // be called and we have the same problem as in GetEnumerator() below. Also, DeserializeAllItems
                // take the lock to ensure consistency - which it does within.
                this.DeserializeAllItems();
                return base.Keys;
            }
        }

        /// <summary>Creates a new, empty <see cref="T:System.Web.SessionState.SessionStateItemCollection" /> object.</summary>
        public ConcurrentSessionStateItemCollection() : base(Misc.CaseInsensitiveInvariantKeyComparer)
        {
        }

        /// <summary>Removes all values and keys from the session-state collection.</summary>
        public void Clear()
        {
            lock (this._serializedItemsLock)
            {
                if (this._serializedItems != null)
                {
                    this._serializedItems.Clear();
                }
                base.BaseClear();
                this._dirty = true;
            }
        }

        /// <summary>Returns an enumerator that can be used to read all the key names in the collection.</summary>
        /// <returns>An <see cref="T:System.Collections.IEnumerator" /> that can iterate through the variable names in the session-state collection.</returns>
        public override IEnumerator GetEnumerator()
        {
            // Have to deserialize all items; otherwise the enumerator won't work because we'll keep
            // on changing the collection during individual item deserialization. Also, DeserializeAllItems
            // take the lock to ensure consistency - which it does within.
            this.DeserializeAllItems();
            return base.GetEnumerator();
        }

        internal static bool IsImmutable(object o)
        {
            return Misc.ImmutableTypes[o.GetType()] != null;
        }

        /// <summary>Deletes an item from the collection.</summary>
        /// <param name="name">The name of the item to delete from the collection. </param>
        public void Remove(string name)
        {
            lock (this._serializedItemsLock)
            {
                if (this._serializedItems != null)
                {
                    this._serializedItems.Remove(name);
                }
                base.BaseRemove(name);
                this._dirty = true;
            }
        }

        /// <summary>Deletes an item at a specified index from the collection.</summary>
        /// <param name="index">The index of the item to remove from the collection. </param>
        /// <exception cref="T:System.ArgumentOutOfRangeException">
        ///   <paramref name="index" /> is less than zero.- or -<paramref name="index" /> is equal to or greater than <see cref="P:System.Collections.ICollection.Count" />.</exception>
        public void RemoveAt(int index)
        {
            lock (this._serializedItemsLock)
            {
                if (this._serializedItems != null && index < this._serializedItems.Count)
                {
                    this._serializedItems.RemoveAt(index);
                }
                base.BaseRemoveAt(index);
                this._dirty = true;
            }
        }

        private void DeserializeAllItems()
        {
            if (_serializedItems == null)
            {
                return;
            }

            lock (_serializedItemsLock)
            {
                for (int i = 0; i < _serializedItems.Count; i++)
                {
                    DeserializeItem(_serializedItems.GetKey(i), false);
                }
            }
        }

        private void DeserializeItem(int index)
        {
            // No-op if SessionStateItemCollection is not deserialized from a persistent storage.
            if (_serializedItems == null)
            {
                return;
            }

            lock (_serializedItemsLock)
            {
                // No-op if the item isn't serialized.
                if (index >= _serializedItems.Count)
                {
                    return;
                }

                DeserializeItem(_serializedItems.GetKey(index), false);
            }
        }

        private void DeserializeItem(String name, bool check)
        {
            object val;

            lock (_serializedItemsLock)
            {
                if (check)
                {
                    // No-op if SessionStateItemCollection is not deserialized from a persistent storage,
                    if (_serializedItems == null)
                    {
                        return;
                    }

                    // User is asking for an item we don't have.
                    if (!_serializedItems.ContainsKey(name))
                    {
                        return;
                    }
                }

                Debug.Assert(_serializedItems != null);
                Debug.Assert(_stream != null);

                SerializedItemPosition position = (SerializedItemPosition)_serializedItems[name];
                if (position.IsDeserialized)
                {
                    // It has been deserialized already.
                    return;
                }

                // Position the stream to the place where the item is stored.
                _stream.Seek(position.Offset, SeekOrigin.Begin);
                val = StateSerializationUtil.ReadValueFromStream(new BinaryReader(_stream));

                BaseSet(name, val);

                // At the end, mark the item as deserialized by making the offset -1
                position.MarkDeserializedOffsetAndCheck();
            }
        }

        private void MarkItemDeserialized(String name)
        {
            // No-op if SessionStateItemCollection is not deserialized from a persistent storage,
            if (_serializedItems == null)
            {
                return;
            }

            lock (_serializedItemsLock)
            {
                // If the serialized collection contains this key, mark it deserialized
                if (_serializedItems.ContainsKey(name))
                {
                    // Mark the item as deserialized by making it -1.
                    ((SerializedItemPosition)_serializedItems[name]).MarkDeserializedOffset();
                }
            }
        }

        private void MarkItemDeserialized(int index)
        {
            // No-op if SessionStateItemCollection is not deserialized from a persistent storage,
            if (_serializedItems == null)
            {
                return;
            }

            lock (_serializedItemsLock)
            {
                // No-op if the item isn't serialized.
                if (index >= _serializedItems.Count)
                {
                    return;
                }

               ((SerializedItemPosition)_serializedItems[index]).MarkDeserializedOffset();
            }
        }

        private class KeyedCollection : NameObjectCollectionBase
        {
            internal object this[string name]
            {
                get
                {
                    return base.BaseGet(name);
                }
                set
                {
                    if (base.BaseGet(name) == null && value == null)
                    {
                        return;
                    }
                    base.BaseSet(name, value);
                }
            }

            internal object this[int index]
            {
                get
                {
                    return base.BaseGet(index);
                }
            }

            internal KeyedCollection(int count) : base(count, Misc.CaseInsensitiveInvariantKeyComparer)
            {
            }

            internal void Clear()
            {
                base.BaseClear();
            }

            internal bool ContainsKey(string name)
            {
                return base.BaseGet(name) != null;
            }

            internal string GetKey(int index)
            {
                return base.BaseGetKey(index);
            }

            internal void Remove(string name)
            {
                base.BaseRemove(name);
            }

            internal void RemoveAt(int index)
            {
                base.BaseRemoveAt(index);
            }
        }

        /// <summary>
        /// Serializes the session state item collection to a stream.
        /// </summary>
        /// <param name="writer"></param>
        public void Serialize(BinaryWriter writer)
        {
            int count;
            int i;
            long iOffsetStart;
            long iValueStart;
            string key;
            object value;
            long curPos;
            byte[] buffer = null;
            Stream baseStream = writer.BaseStream;

            lock (_serializedItemsLock)
            {
                count = Count;
                writer.Write(count);

                if (count > 0)
                {
                    if (BaseGet(null) != null)
                    {
                        // We have a value with a null key.  Find its index.
                        for (i = 0; i < count; i++)
                        {
                            key = BaseGetKey(i);
                            if (key == null)
                            {
                                writer.Write(i);
                                break;
                            }
                        }

                        Debug.Assert(i != count);
                    }
                    else
                    {
                        writer.Write(NO_NULL_KEY);
                    }

                    // Write out all the keys.
                    for (i = 0; i < count; i++)
                    {
                        key = BaseGetKey(i);
                        if (key != null)
                        {
                            writer.Write(key);
                        }
                    }

                    // Next, allocate space to store the offset:
                    // - We won't store the offset of first item because it's always zero.
                    // - The offset of an item is counted from the beginning of serialized values
                    // - But we will store the offset of the first byte off the last item because
                    //   we need that to calculate the size of the last item.
                    iOffsetStart = baseStream.Position;
                    baseStream.Seek(SIZE_OF_INT32 * count, SeekOrigin.Current);

                    iValueStart = baseStream.Position;

                    for (i = 0; i < count; i++)
                    {
                        // See if that item has not be deserialized yet.
                        if (_serializedItems != null &&
                            i < _serializedItems.Count &&
                            !((SerializedItemPosition)_serializedItems[i]).IsDeserialized)
                        {

                            SerializedItemPosition position = (SerializedItemPosition)_serializedItems[i];

                            Debug.Assert(_stream != null);

                            // The item is read as serialized data from a store, and it's still
                            // serialized, meaning no one has referenced it.  Just copy
                            // the bytes over.

                            // Move the stream to the serialized data and copy it over to writer
                            _stream.Seek(position.Offset, SeekOrigin.Begin);

                            if (buffer == null || buffer.Length < position.DataLength)
                            {
                                buffer = new Byte[position.DataLength];
                            }

                            _stream.Read(buffer, 0, position.DataLength);

                            baseStream.Write(buffer, 0, position.DataLength);
                        }
                        else
                        {
                            value = BaseGet(i);
                            StateSerializationUtil.WriteValueToStream(value, writer);
                        }

                        curPos = baseStream.Position;

                        // Write the offset
                        baseStream.Seek(i * SIZE_OF_INT32 + iOffsetStart, SeekOrigin.Begin);
                        writer.Write((int)(curPos - iValueStart));

                        // Move back to current position
                        baseStream.Seek(curPos, SeekOrigin.Begin);
                    }
                }
            }
        }

        /// <summary>
        /// Deserializes the session state item collection from a stream.
        /// </summary>
        /// <param name="reader"></param>
        /// <returns></returns>
        /// <exception cref="HttpException"></exception>
        public static ConcurrentSessionStateItemCollection Deserialize(BinaryReader reader)
        {
            ConcurrentSessionStateItemCollection d = new ConcurrentSessionStateItemCollection();
            int count;
            int nullKey;
            String key;
            int i;
            byte[] buffer;

            count = reader.ReadInt32();

            if (count > 0)
            {
                nullKey = reader.ReadInt32();

                d._serializedItems = new KeyedCollection(count);

                // First, deserialize all the keys
                for (i = 0; i < count; i++)
                {
                    if (i == nullKey)
                    {
                        key = null;
                    }
                    else
                    {
                        key = reader.ReadString();
                    }

                    // Need to set them with null value first, so that
                    // the order of them items is correct.
                    d.BaseSet(key, null);
                }

                // Next, deserialize all the offsets
                // First offset will be 0, and the data length will be the first read offset
                int offset0 = reader.ReadInt32();
                d._serializedItems[d.BaseGetKey(0)] = new SerializedItemPosition(0, offset0);

                int offset1 = 0;
                for (i = 1; i < count; i++)
                {
                    offset1 = reader.ReadInt32();
                    d._serializedItems[d.BaseGetKey(i)] = new SerializedItemPosition(offset0, offset1 - offset0);
                    offset0 = offset1;
                }

                d._iLastOffset = offset0;

                // _iLastOffset is the first byte past the last item, which equals
                // the total length of all serialized data
                buffer = new byte[d._iLastOffset];
                int bytesRead = reader.BaseStream.Read(buffer, 0, d._iLastOffset);
                if (bytesRead != d._iLastOffset)
                {
                    throw new HttpException(String.Format(CultureInfo.CurrentCulture, SR.Invalid_session_state));
                }
                d._stream = new MemoryStream(buffer);
            }

            d._dirty = false;

            return d;
        }

        private sealed class SerializedItemPosition
        {
            int _offset;
            int _dataLength;

            internal SerializedItemPosition(int offset, int dataLength)
            {
                this._offset = offset;
                this._dataLength = dataLength;
            }

            internal int Offset
            {
                get { return _offset; }
            }

            internal int DataLength
            {
                get { return _dataLength; }
            }

            // Mark the item as deserialized by making the offset -1.
            internal void MarkDeserializedOffset()
            {
                _offset = -1;
            }

            internal void MarkDeserializedOffsetAndCheck()
            {
                if (_offset >= 0)
                {
                    MarkDeserializedOffset();
                }
                else
                {
                    Debug.Fail("Offset shouldn't be negative inside MarkDeserializedOffsetAndCheck.");
                }
            }

            internal bool IsDeserialized
            {
                get { return _offset < 0; }
            }
        }
    }

    internal sealed class Misc
    {
        private static Hashtable s_immutableTypes;
        internal static Hashtable ImmutableTypes => s_immutableTypes;

        private static StringComparer s_caseInsensitiveInvariantKeyComparer;

        internal static StringComparer CaseInsensitiveInvariantKeyComparer
        {
            get
            {
                if (Misc.s_caseInsensitiveInvariantKeyComparer == null)
                {
                    Misc.s_caseInsensitiveInvariantKeyComparer = StringComparer.Create(CultureInfo.InvariantCulture, true);
                }
                return Misc.s_caseInsensitiveInvariantKeyComparer;
            }
        }

        static Misc()
        {
            s_immutableTypes = new Hashtable(19);
            Type type = typeof(string);
            s_immutableTypes.Add(type, type);
            type = typeof(int);
            s_immutableTypes.Add(type, type);
            type = typeof(bool);
            s_immutableTypes.Add(type, type);
            type = typeof(DateTime);
            s_immutableTypes.Add(type, type);
            type = typeof(decimal);
            s_immutableTypes.Add(type, type);
            type = typeof(byte);
            s_immutableTypes.Add(type, type);
            type = typeof(char);
            s_immutableTypes.Add(type, type);
            type = typeof(float);
            s_immutableTypes.Add(type, type);
            type = typeof(double);
            s_immutableTypes.Add(type, type);
            type = typeof(sbyte);
            s_immutableTypes.Add(type, type);
            type = typeof(short);
            s_immutableTypes.Add(type, type);
            type = typeof(long);
            s_immutableTypes.Add(type, type);
            type = typeof(ushort);
            s_immutableTypes.Add(type, type);
            type = typeof(uint);
            s_immutableTypes.Add(type, type);
            type = typeof(ulong);
            s_immutableTypes.Add(type, type);
            type = typeof(TimeSpan);
            s_immutableTypes.Add(type, type);
            type = typeof(Guid);
            s_immutableTypes.Add(type, type);
            type = typeof(IntPtr);
            s_immutableTypes.Add(type, type);
            type = typeof(UIntPtr);
            s_immutableTypes.Add(type, type);
        }

        public Misc() { }
    }
}