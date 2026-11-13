// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Models
{
    /// <summary>
    /// A thread-safe implementation of a list backed by <see cref="List{T}"/> with lock-based synchronization.
    /// Supports concurrent reads and writes.
    /// </summary>
    /// <typeparam name="T">The type of elements in the list.</typeparam>
    internal class ThreadSafeList<T> : ICollection<T>
    {
        private readonly List<T> _list;
        private readonly object _lock = new object();

        /// <summary>
        /// Initializes a new instance of the <see cref="ThreadSafeList{T}"/> class.
        /// </summary>
        public ThreadSafeList()
        {
            _list = new List<T>();
        }

        /// <inheritdoc/>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _list.Count;
                }
            }
        }

        /// <inheritdoc/>
        public bool IsReadOnly => false;

        /// <inheritdoc/>
        public void Add(T item)
        {
            lock (_lock)
            {
                _list.Add(item);
            }
        }

        /// <inheritdoc/>
        public void Clear()
        {
            lock (_lock)
            {
                _list.Clear();
            }
        }

        /// <inheritdoc/>
        public bool Contains(T item)
        {
            lock (_lock)
            {
                return _list.Contains(item);
            }
        }

        /// <inheritdoc/>
        public void CopyTo(T[] array, int arrayIndex)
        {
            lock (_lock)
            {
                _list.CopyTo(array, arrayIndex);
            }
        }

        /// <inheritdoc/>
        public IEnumerator<T> GetEnumerator()
        {
            List<T> snapshot;
            lock (_lock)
            {
                snapshot = new List<T>(_list);
            }

            return snapshot.GetEnumerator();
        }

        /// <inheritdoc/>
        public bool Remove(T item)
        {
            lock (_lock)
            {
                return _list.Remove(item);
            }
        }

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
