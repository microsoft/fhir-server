// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Models
{
    public class ListedContactPoint : IEquatable<ListedContactPoint>
    {
        public string System { get; set; }

        public string Use { get; set; }

        public string Value { get; set; }

        public bool Equals(ListedContactPoint other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return System == other.System && Use == other.Use && Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj.GetType() != GetType())
            {
                return false;
            }

            return Equals((ListedContactPoint)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = System != null ? System.GetHashCode(StringComparison.Ordinal) : 0;
                hashCode = (hashCode * 397) ^ (Use != null ? Use.GetHashCode(StringComparison.Ordinal) : 0);
                hashCode = (hashCode * 397) ^ (Value != null ? Value.GetHashCode(StringComparison.Ordinal) : 0);

                return hashCode;
            }
        }
    }
}
