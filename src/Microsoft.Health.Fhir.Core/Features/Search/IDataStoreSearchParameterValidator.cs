// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// Used to validate whether a SearchParameter is supported by the data store
    /// beyond the standard validation which occurs, specific data store implementations
    /// may have have additional restrictions
    /// </summary>
    public interface IDataStoreSearchParameterValidator
    {
        /// <summary>
        /// Determines whether the SearchParameter is supported.
        /// </summary>
        /// <param name="searchParameter">The search parameter to check for support</param>
        /// <param name="errorMessage">If the method returns <c>false</c>, includes an error message explaining that the SearchParameter is not supported</param>
        /// <returns>Whether the SearchParameter supported</returns>
        bool ValidateSearchParameter(SearchParameterInfo searchParameter, out string errorMessage);
    }
}
