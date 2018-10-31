// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Definition
{
    /// <summary>
    /// Methods to access compartment definitions
    /// </summary>
    public interface ICompartmentDefinitionManager
    {
        /// <summary>
        /// Get the compartment index for the given <paramref name="resourceType"/>.
        /// </summary>
        /// <param name="resourceType">The fhir resource type for which to get the index.</param>
        /// <returns>The index of compartment type to fieldnames that represent the <paramref name="resourceType"/> in a compartment.</returns>
        Dictionary<CompartmentType, HashSet<string>> GetCompartmentSearchParams(ResourceType resourceType);

        /// <summary>
        /// Get the compartment index for the given <paramref name="resourceType"/>.
        /// </summary>
        /// <param name="resourceType">The fhir resource type for which to get the index.</param>
        /// <param name="compartmentSearchParams">On return, the index of compartment type to the fieldnames that represent the <paramref name="resourceType"/> in a compartment if it exists. Otherwise the default value.</param>
        /// <returns><c>true</c> if the compartmentSearchParams exists; otherwise, <c>false</c>.</returns>
        bool TryGetCompartmentSearchParams(ResourceType resourceType, out Dictionary<CompartmentType, HashSet<string>> compartmentSearchParams);
    }
}
