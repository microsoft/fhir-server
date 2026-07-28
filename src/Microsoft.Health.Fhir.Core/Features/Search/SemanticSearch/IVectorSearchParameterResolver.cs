// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Resolves the FHIR SearchParameters enabled for vector indexing.
    /// </summary>
    public interface IVectorSearchParameterResolver
    {
        /// <summary>
        /// Gets enabled vector SearchParameters applicable to a FHIR resource type.
        /// </summary>
        /// <param name="resourceType">The FHIR resource type.</param>
        /// <returns>The applicable vector SearchParameters.</returns>
        IReadOnlyList<SearchParameterInfo> GetSearchParameters(string resourceType);

        /// <summary>
        /// Gets and validates an enabled vector SearchParameter by canonical URI.
        /// </summary>
        /// <param name="canonicalUri">The SearchParameter canonical URI.</param>
        /// <returns>The resolved SearchParameter.</returns>
        SearchParameterInfo GetSearchParameter(Uri canonicalUri);
    }
}
