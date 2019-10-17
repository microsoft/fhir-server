// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Conformance.Schema;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Models
{
    public class OperationComponent : IEquatable<OperationComponent>
    {
        [SchemaConst]
        public string Name { get; set; }

        public string Definition { get; set; }

        public bool Equals(OperationComponent other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return Name == other.Name && Definition == other.Definition;
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

            return Equals((OperationComponent)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Name != null ? Name.GetHashCode(StringComparison.Ordinal) : 0) * 397) ^ (Definition != null ? Definition.GetHashCode(StringComparison.Ordinal) : 0);
            }
        }
    }
}
