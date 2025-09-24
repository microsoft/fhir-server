// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Partitioning
{
    /// <summary>
    /// Defines system resource types that are shared across all partitions.
    /// These resources are stored in the 'system' partition but appear available from any partition context.
    /// </summary>
    public static class SystemResourceTypes
    {
        /// <summary>
        /// Set of resource types that are considered system resources and shared across partitions.
        /// </summary>
        public static readonly HashSet<string> Types = new()
        {
            "SearchParameter",
            "OperationDefinition",
            "StructureDefinition",
            "ValueSet",
            "CodeSystem",
            "CapabilityStatement",
            "CompartmentDefinition",
        };

        /// <summary>
        /// Array version for performance when iteration is needed.
        /// </summary>
        public static readonly string[] TypeArray =
        {
            "SearchParameter",
            "OperationDefinition",
            "StructureDefinition",
            "ValueSet",
            "CodeSystem",
            "CapabilityStatement",
            "CompartmentDefinition",
        };

        /// <summary>
        /// Determines if the specified resource type is a system resource.
        /// </summary>
        /// <param name="resourceType">The resource type to check.</param>
        /// <returns>True if the resource type is a system resource, false otherwise.</returns>
        public static bool IsSystemResource(string resourceType)
        {
            return !string.IsNullOrEmpty(resourceType) && Types.Contains(resourceType);
        }
    }
}
