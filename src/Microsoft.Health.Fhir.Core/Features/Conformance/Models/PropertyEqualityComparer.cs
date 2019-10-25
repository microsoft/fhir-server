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
        private readonly Func<T, string>[] _propertiesToCompare;

        public PropertyEqualityComparer(params Func<T, string>[] propertiesToCompare)
        {
            EnsureArg.IsNotNull(propertiesToCompare, nameof(propertiesToCompare));

            _propertiesToCompare = propertiesToCompare;
        }

        public bool Equals(T x, T y)
        {
            if (x == null || y == null)
            {
                return false;
            }

            return _propertiesToCompare.All(property => ReferenceEquals(property(x), property(y)) || property(x) == property(y));
        }

        public int GetHashCode(T obj)
        {
            var hash = default(HashCode);

            foreach (Func<T, string> prop in _propertiesToCompare)
            {
                hash.Add(prop(obj));
            }

            return hash.ToHashCode();
        }
    }
}
