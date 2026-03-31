// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Models
{
    /// <summary>
    /// A thread-safe implementation of a hash set backed by <see cref="ConcurrentDictionary{TKey, TValue}"/>.
    /// Supports concurrent reads and writes without external synchronization.
    /// </summary>
    /// <typeparam name="T">The type of elements in the set.</typeparam>
    public class ThreadSafeHashSet<T> : ICollection<T>
    {
        private readonly ConcurrentDictionary<T, byte> _set;

        /// <summary>
        /// Initializes a new instance of the <see cref="ThreadSafeHashSet{T}"/> class.
        /// </summary>
        public ThreadSafeHashSet()
        {
            _set = new ConcurrentDictionary<T, byte>();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ThreadSafeHashSet{T}"/> class
        /// that uses the specified <see cref="IEqualityComparer{T}"/>.
        /// </summary>
        /// <param name="equalityComparer">The equality comparer to use when comparing elements.</param>
        public ThreadSafeHashSet(IEqualityComparer<T> equalityComparer)
        {
            _set = new ConcurrentDictionary<T, byte>(equalityComparer);
        }

        /// <inheritdoc />
        public int Count => _set.Count;

        /// <inheritdoc />
        public bool IsReadOnly => false;

        /// <inheritdoc />
        public void Add(T item)
        {
            TryAdd(item);
        }

        public bool TryAdd(T item)
        {
            return _set.TryAdd(item, 1);
        }

        /// <inheritdoc />
        public void Clear()
        {
            _set.Clear();
        }

        /// <inheritdoc />
        public bool Contains(T item)
        {
            return _set.ContainsKey(item);
        }

        /// <inheritdoc />
        public void CopyTo(T[] array, int arrayIndex)
        {
            _set.Keys.CopyTo(array, arrayIndex);
        }

        /// <inheritdoc />
        public IEnumerator<T> GetEnumerator()
        {
            return _set.Keys.GetEnumerator();
        }

        /// <inheritdoc />
        public bool Remove(T item)
        {
            return _set.TryRemove(item, out _);
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
