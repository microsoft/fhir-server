// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Parameters
{
    public interface ISearchParameterSupportResolver
    {
        /// <summary>
        /// Determines if the given search parameter is able to be indexed
        /// </summary>
        /// <param name="parameterInfo">The search parameter to check.</param>
        /// <returns name="Supported">True if one or more matching converters is found.</returns>
        /// <returns name="IsPartiallySupported">False if all matching converters are found or no matching converters are found, true otherwise.</returns>
        /// <returns name="ErrorMessage">Provides more detail if a search parameter is not supported.</returns>
        (bool Supported, bool IsPartiallySupported, string ErrorMessage) IsSearchParameterSupported(SearchParameterInfo parameterInfo);
    }
}
