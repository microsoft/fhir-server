// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Models
{
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
            foreach (var property in _propertiesToCompare)
            {
                if (ReferenceEquals(null, property(x)))
                {
                    return false;
                }

                if (!ReferenceEquals(property(x), property(y)) && property(x) != property(y))
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(T obj)
        {
            return obj.GetHashCode();
        }
    }
}
