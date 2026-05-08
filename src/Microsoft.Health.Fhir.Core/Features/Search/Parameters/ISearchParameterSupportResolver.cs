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
        /// <param name="parameterInfo">Search Parameter info</param>
        (bool Supported, bool IsPartiallySupported) IsSearchParameterSupported(SearchParameterInfo parameterInfo);
    }
}
