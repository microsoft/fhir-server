// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class ResourceTypeExtensions
    {
        public static Type ToResourceModelType(this ResourceElement resourceType)
        {
            EnsureArg.IsNotNull(resourceType, nameof(resourceType));
            return ModelInfoProvider.GetTypeForFhirType(resourceType.InstanceType);
        }

        public static ResourceElement ToResourceElement(this ITypedElement typedElement)
        {
            return new ResourceElement(typedElement);
        }

        /*
        /// <summary>
        /// Compares a ResourceType with another value, using either direct equality or string comparison based on the LegacyTypeComparisonFlag.
        /// </summary>
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
        */
    }
}
