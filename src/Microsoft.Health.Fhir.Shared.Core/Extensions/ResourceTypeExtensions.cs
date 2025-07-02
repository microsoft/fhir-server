// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class ResourceTypeExtensions
    {
        public static ResourceElement ToResourceElement(this Resource resource)
        {
            return new ResourceElement(resource.ToTypedElement(), resource);
        }

        public static bool EqualsString(this ResourceType resourceType, string value, bool ignoreCase = true)
        {
            return string.Equals(resourceType.ToString(), value, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }

        /// <summary>
        /// Compares a ResourceType with another value, using either direct equality or string comparison based on the compile-time flag.
        /// </summary>
        /// <remarks>
        /// When USE_HL7_LEGACY_PACKAGES is defined, this method uses direct type comparison (r.Type == ResourceType.X),
        /// otherwise it falls back to string-based comparison with EqualsString.
        /// This allows toggling between implementations to compare performance characteristics.
        /// </remarks>
        /// <param name="resourceType">The resource type to compare.</param>
        /// <param name="other">The other value to compare with, either a ResourceType or a string representation.</param>
        /// <returns>True if the values are equal, false otherwise.</returns>
        public static bool CompareResourceType(this ResourceType resourceType, object other)
        {
#if USE_HL7_LEGACY_PACKAGES
            // Legacy implementation: direct type comparison
            if (other is ResourceType otherType)
            {
                return resourceType == otherType;
            }

            return false;
#else
            // Current implementation: string comparison
            return resourceType.EqualsString(other?.ToString());
#endif
        }
    }
}
