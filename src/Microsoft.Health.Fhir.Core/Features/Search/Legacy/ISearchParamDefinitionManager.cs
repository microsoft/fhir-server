// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy
{
    /// <summary>
    /// Defines mechanisms for getting search parameter definition.
    /// </summary>
    public interface ISearchParamDefinitionManager
    {
        /// <summary>
        /// Gets the search parameter type.
        /// </summary>
        /// <param name="resourceType">The resource type that contains the search paramter.</param>
        /// <param name="paramName">The search parameter name.</param>
        /// <returns>The search parameter type.</returns>
        SearchParamType GetSearchParamType(Type resourceType, string paramName);

        /// <summary>
        /// Gets the target resource types for a reference search parameter.
        /// </summary>
        /// <param name="resourceType">The resource type that contains the search parameter.</param>
        /// <param name="paramName">The search parameter name.</param>
        /// <returns>Target resource types for the given reference search parameter.</returns>
        IReadOnlyCollection<Type> GetReferenceTargetResourceTypes(Type resourceType, string paramName);
    }
}
