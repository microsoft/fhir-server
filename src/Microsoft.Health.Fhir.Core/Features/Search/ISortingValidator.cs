// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// Used to validate whether a sorting specification requested by the client is supported by the data store
    /// </summary>
    public interface ISortingValidator
    {
        /// <summary>
        /// Determines whether the requesting sorting is supported.
        /// </summary>
        /// <param name="sorting">The search parameters to sort by</param>
        /// <param name="errorMessages">If the method returns <c>false</c>, error messages explaining why the requested sorting is not supported</param>
        /// <returns>Whether the requested sorting is supported</returns>
        bool ValidateSorting(IReadOnlyList<(SearchParameterInfo searchParameter, SortOrder sortOrder)> sorting, out IReadOnlyList<string> errorMessages);
    }
}
