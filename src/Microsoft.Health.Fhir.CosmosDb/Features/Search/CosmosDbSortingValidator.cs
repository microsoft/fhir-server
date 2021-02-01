// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Search
{
    internal class CosmosDbSortingValidator : ISortingValidator
    {
        public bool ValidateSorting(IReadOnlyList<(SearchParameterInfo searchParameter, SortOrder sortOrder)> sorting, out IReadOnlyList<string> errorMessages)
        {
            EnsureArg.IsNotNull(sorting, nameof(sorting));

            switch (sorting)
            {
                case { Count: 0 }:
                case { Count: 1 } when sorting[0] is { searchParameter: { Code: KnownQueryParameterNames.LastUpdated } }:
                    errorMessages = Array.Empty<string>();
                    return true;
                case { Count: 1 }:
                    errorMessages = new[] { string.Format(CultureInfo.InvariantCulture, Core.Resources.SearchSortParameterNotSupported, sorting[0].searchParameter.Code) };
                    return false;
                default:
                    errorMessages = new[] { Core.Resources.MultiSortParameterNotSupported };
                    return false;
            }
        }
    }
}
