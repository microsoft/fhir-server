// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.SqlServer.Features.Schema;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    internal class SqlServerSortingValidator : ISortingValidator
    {
        private readonly SchemaInformation _schemaInformation;

        internal static readonly HashSet<SearchParamType> SupportedSortParamTypes = new HashSet<SearchParamType>()
        {
            SearchParamType.Date,
            SearchParamType.String,
        };

        public SqlServerSortingValidator(SchemaInformation schemaInformation)
        {
            _schemaInformation = schemaInformation;
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
        }

        public bool ValidateSorting(IReadOnlyList<(SearchParameterInfo searchParameter, SortOrder sortOrder)> sorting, out IReadOnlyList<string> errorMessages)
        {
            EnsureArg.IsNotNull(sorting, nameof(sorting));

            switch (sorting)
            {
                case { Count: 0 }:
                case { Count: 1 } when SupportedSortParamTypes.Contains(sorting[0].searchParameter.Type):
                    errorMessages = Array.Empty<string>();
                    return true;
                case { Count: 1 }:
                    errorMessages = new[] { string.Format(CultureInfo.InvariantCulture, Core.Resources.SearchSortParameterNotSupported, sorting[0].searchParameter.Code) };
                    return false;
                case { Count: 2 } when _schemaInformation.Current >= SchemaVersionConstants.PartitionedTables &&
                                       sorting[0].searchParameter.Url.Equals(SearchParameterNames.ResourceTypeUri) &&
                                       sorting[1].searchParameter.Url.Equals(SearchParameterNames.LastUpdatedUri):

                    if (sorting[0].sortOrder != sorting[1].sortOrder)
                    {
                        errorMessages = new[] { Resources.TypeAndLastUpdatedMustHaveSameSortDirection };
                        return false;
                    }

                    errorMessages = Array.Empty<string>();
                    return true;
                default:
                    errorMessages = new[] { Resources.OnlyTypeAndLastUpdatedSupportedForCompoundSort };
                    return false;
            }
        }
    }
}
