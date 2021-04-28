// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using EnsureThat;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Search
{
    internal class CosmosDbSortingValidator : ISortingValidator
    {
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;

        public CosmosDbSortingValidator(RequestContextAccessor<IFhirRequestContext> contextAccessor)
        {
            EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            _contextAccessor = contextAccessor;
        }

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
                    (SearchParameterInfo searchParameter, SortOrder sortOrder) parameter = sorting[0];

                    if (parameter.searchParameter.SortStatus == SortParameterStatus.Enabled ||
                        (parameter.searchParameter.SortStatus == SortParameterStatus.Supported && _contextAccessor.RequestContext?.RequestHeaders.TryGetValue(KnownHeaders.PartiallyIndexedParamsHeaderName, out StringValues _) == true))
                    {
                        errorMessages = Array.Empty<string>();
                        return true;
                    }

                    errorMessages = new[] { string.Format(CultureInfo.InvariantCulture, Core.Resources.SearchSortParameterNotSupported, parameter.searchParameter.Code) };
                    return false;
                default:
                    errorMessages = new[] { Core.Resources.MultiSortParameterNotSupported };
                    return false;
            }
        }
    }
}
