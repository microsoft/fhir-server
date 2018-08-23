// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy
{
    /// <summary>
    /// Parses the given string to an instance of <see cref="ISearchValue"/>.
    /// </summary>
    /// <param name="s">The string to be parsed.</param>
    /// <returns>An instance of <see cref="ISearchValue"/>.</returns>
    public delegate ISearchValue SearchParamValueParser(string s);
}
