// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Models
{
    /// <summary>
    /// Creates an IEqualityComparer for an object based on a number of properties.
    /// </summary>
    /// <typeparam name="T">Type to compare</typeparam>
    internal class PropertyEqualityComparer<T> : IEqualityComparer<T>
    {
        private readonly StringComparison _stringComparison;
        private readonly Func<T, string>[] _propertiesToCompare;

        public PropertyEqualityComparer(StringComparison stringComparison, params Func<T, string>[] propertiesToCompare)
        {
            EnsureArg.IsNotNull(propertiesToCompare, nameof(propertiesToCompare));

            _stringComparison = stringComparison;
            _propertiesToCompare = propertiesToCompare;
        }

        public PropertyEqualityComparer(params Func<T, string>[] propertiesToCompare)
            : this(StringComparison.Ordinal, propertiesToCompare)
        {
        }

        public bool Equals(T x, T y)
        {
            if (x == null || y == null)
            {
                return false;
            }

            return _propertiesToCompare.All(property => string.Equals(property(x), property(y), _stringComparison));
        }

        public int GetHashCode(T obj)
        {
            EnsureArg.IsNotNull<object>(obj, nameof(obj));

            var hash = default(HashCode);

            foreach (Func<T, string> prop in _propertiesToCompare)
            {
                hash.Add(prop(obj));
            }

            return hash.ToHashCode();
        }
    }
}
