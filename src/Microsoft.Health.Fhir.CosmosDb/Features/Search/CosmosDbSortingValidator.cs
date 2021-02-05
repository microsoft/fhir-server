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
using Microsoft.Health.Fhir.CosmosDb.Configs;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Search
{
    internal class CosmosDbSortingValidator : ISortingValidator
    {
        private readonly CosmosDataStoreConfiguration _configuration;

        public CosmosDbSortingValidator(CosmosDataStoreConfiguration configuration)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            _configuration = configuration;
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

                    if (_configuration.SortFields.Contains(parameter.searchParameter.Url.ToString()))
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
