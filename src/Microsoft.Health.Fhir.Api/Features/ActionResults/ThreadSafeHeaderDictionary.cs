// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Health.Fhir.Api.Features.ActionResults
{
    /// <summary>
    /// A thread-safe implementation of <see cref="IHeaderDictionary"/> backed by <see cref="ConcurrentDictionary{TKey, TValue}"/>.
    /// Supports concurrent reads and writes without external synchronization.
    /// </summary>
    internal class ThreadSafeHeaderDictionary : IHeaderDictionary
    {
        private readonly ConcurrentDictionary<string, StringValues> _headers;

        /// <summary>
        /// Initializes a new instance of the <see cref="ThreadSafeHeaderDictionary"/> class.
        /// </summary>
        public ThreadSafeHeaderDictionary()
        {
            _headers = new ConcurrentDictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        public long? ContentLength
        {
            get
            {
                if (_headers.TryGetValue("Content-Length", out StringValues rawValue) &&
                    long.TryParse(rawValue.ToString(), NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out long parsed))
                {
                    return parsed;
                }

                return null;
            }

            set
            {
                if (value.HasValue)
                {
                    _headers["Content-Length"] = new StringValues(value.Value.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    _headers.TryRemove("Content-Length", out _);
                }
            }
        }

        /// <inheritdoc />
        public int Count => _headers.Count;

        /// <inheritdoc />
        public bool IsReadOnly => false;

        /// <inheritdoc />
        public ICollection<string> Keys => _headers.Keys;

        /// <inheritdoc />
        public ICollection<StringValues> Values => _headers.Values;

        /// <inheritdoc />
        public StringValues this[string key]
        {
            get => _headers.TryGetValue(key, out StringValues value) ? value : StringValues.Empty;
            set
            {
                if (StringValues.IsNullOrEmpty(value))
                {
                    _headers.TryRemove(key, out _);
                }
                else
                {
                    _headers[key] = value;
                }
            }
        }

        /// <inheritdoc />
        public void Add(string key, StringValues value)
        {
            if (!_headers.TryAdd(key, value))
            {
                throw new ArgumentException($"An item with the key '{key}' has already been added.");
            }
        }

        /// <inheritdoc />
        public void Add(KeyValuePair<string, StringValues> item)
        {
            Add(item.Key, item.Value);
        }

        /// <inheritdoc />
        public void Clear()
        {
            _headers.Clear();
        }

        /// <inheritdoc />
        public bool Contains(KeyValuePair<string, StringValues> item)
        {
            return _headers.TryGetValue(item.Key, out StringValues value) && value.Equals(item.Value);
        }

        /// <inheritdoc />
        public bool ContainsKey(string key)
        {
            return _headers.ContainsKey(key);
        }

        /// <inheritdoc />
        public void CopyTo(KeyValuePair<string, StringValues>[] array, int arrayIndex)
        {
            ((ICollection<KeyValuePair<string, StringValues>>)_headers).CopyTo(array, arrayIndex);
        }

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator()
        {
            return _headers.GetEnumerator();
        }

        /// <inheritdoc />
        public bool Remove(string key)
        {
            return _headers.TryRemove(key, out _);
        }

        /// <inheritdoc />
        public bool Remove(KeyValuePair<string, StringValues> item)
        {
            return ((ICollection<KeyValuePair<string, StringValues>>)_headers).Remove(item);
        }

        /// <inheritdoc />
        public bool TryGetValue(string key, out StringValues value)
        {
            return _headers.TryGetValue(key, out value);
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
