using System;
using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using HttpRuntime = System.Web.HttpRuntime;
using ISessionStateItemCollection = System.Web.SessionState.ISessionStateItemCollection;

namespace Microsoft.AspNet.SessionState
{
    /// <summary>A collection of objects stored in session state. This class cannot be inherited.</summary>
    public sealed class ThreadSafeSessionStateItemCollection : NameObjectCollectionBase, ISessionStateItemCollection, ICollection, IEnumerable
    {
        private static Hashtable s_immutableTypes;

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
                    //this.DeserializeItem(name, true);
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
                    //this.MarkItemDeserialized(name);
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
                    //this.DeserializeItem(index);
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
                    //this.MarkItemDeserialized(index);
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
                //this.DeserializeAllItems();
                return base.Keys;
            }
        }

        static ThreadSafeSessionStateItemCollection()
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

        /// <summary>Creates a new, empty <see cref="T:System.Web.SessionState.SessionStateItemCollection" /> object.</summary>
        public ThreadSafeSessionStateItemCollection() : base(Misc.CaseInsensitiveInvariantKeyComparer)
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
            //this.DeserializeAllItems();
            return base.GetEnumerator();
        }

        internal static bool IsImmutable(object o)
        {
            return s_immutableTypes[o.GetType()] != null;
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

        internal sealed class Misc
        {
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

            public Misc()
            {
            }
        }
    }
}