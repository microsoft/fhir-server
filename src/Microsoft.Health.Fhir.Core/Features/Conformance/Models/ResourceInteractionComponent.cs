// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Conformance.Schema;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Models
{
    public class ResourceInteractionComponent : IEquatable<ResourceInteractionComponent>
    {
        [SchemaConst]
        public string Code { get; set; }

        public string Documentation { get; set; }

        public bool Equals(ResourceInteractionComponent other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return Code == other.Code;
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

            return Equals((ResourceInteractionComponent)obj);
        }

        public override int GetHashCode()
        {
            return Code != null ? Code.GetHashCode(StringComparison.Ordinal) : 0;
        }
    }
}
