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
        private readonly Func<T, string> _propertyToCompare;

        public PropertyEqualityComparer(Func<T, string> propertyToCompare)
        {
            EnsureArg.IsNotNull(propertyToCompare, nameof(propertyToCompare));

            _propertyToCompare = propertyToCompare;
        }

        public bool Equals(T x, T y)
        {
            if (ReferenceEquals(null, x))
            {
                return false;
            }

            if (ReferenceEquals(x, y))
            {
                return true;
            }

            return _propertyToCompare(x) == _propertyToCompare(y);
        }

        public int GetHashCode(T obj)
        {
            return obj.GetHashCode();
        }
    }
}
