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

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    internal class SqlServerSortingValidator : ISortingValidator
    {
        private static readonly HashSet<string> _supportedParameterNames = new HashSet<string>(StringComparer.Ordinal)
        {
            KnownQueryParameterNames.LastUpdated, "birthdate", "date", "abatement-date", "onset-date", "issued", "created", "started", "authoredon",
        };

        public bool ValidateSorting(IReadOnlyList<(SearchParameterInfo searchParameter, SortOrder sortOrder)> sorting, out IReadOnlyList<string> errorMessages)
        {
            EnsureArg.IsNotNull(sorting, nameof(sorting));

            switch (sorting)
            {
                case { Count: 0 }:
                case { Count: 1 } when _supportedParameterNames.Contains(sorting[0].searchParameter.Code):
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
