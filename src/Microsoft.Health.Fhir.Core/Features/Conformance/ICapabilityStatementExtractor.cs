// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{

    /// <summary>
    /// Provides methods to extract resource types, search parameters, and search parameter types from a FHIR capability statement.
    /// </summary>
    public interface ICapabilityStatementExtractor
    {
        /// <summary>
        /// Extracts all resource types from the capability statement.
        /// </summary>
        /// <param name="capabilityStatement">The capability statement to extract from.</param>
        /// <returns>A collection of resource type names.</returns>
        IEnumerable<string> GetResourceTypes(ListedCapabilityStatement capabilityStatement);

        /// <summary>
        /// Extracts search parameters for a specific resource type.
        /// </summary>
        /// <param name="capabilityStatement">The capability statement to extract from.</param>
        /// <param name="resourceType">The resource type to get search parameters for.</param>
        /// <returns>A collection of search parameter information.</returns>
        IEnumerable<CapabilityStatementExtractor.SearchParameterInfo> GetSearchParametersForResource(
            ListedCapabilityStatement capabilityStatement,
            string resourceType);

        /// <summary>
        /// Extracts all search parameters across all resources.
        /// </summary>
        /// <param name="capabilityStatement">The capability statement to extract from.</param>
        /// <returns>A dictionary mapping resource types to their search parameters.</returns>
        IDictionary<string, IEnumerable<CapabilityStatementExtractor.SearchParameterInfo>> GetAllSearchParameters(
            ListedCapabilityStatement capabilityStatement);
    }
}
