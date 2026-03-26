// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Models
{
    public class ThreadSafeHashSet<T> : ICollection<T>
    {
        private readonly ConcurrentDictionary<T, byte> _set;

        public ThreadSafeHashSet()
        {
            _set = new ConcurrentDictionary<T, byte>();
        }

        public ThreadSafeHashSet(IEqualityComparer<T> equalityComparer)
        {
            _set = new ConcurrentDictionary<T, byte>(equalityComparer);
        }

        public int Count => _set.Count;

        public bool IsReadOnly => false;

        public void Add(T item)
        {
            _set.TryAdd(item, 1);
        }

        public void Clear()
        {
            _set.Clear();
        }

        public bool Contains(T item)
        {
            return _set.ContainsKey(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            _set.Keys.CopyTo(array, arrayIndex);
        }

        public IEnumerator<T> GetEnumerator()
        {
            return _set.Keys.GetEnumerator();
        }

        public bool Remove(T item)
        {
            return _set.TryRemove(item, out _);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
