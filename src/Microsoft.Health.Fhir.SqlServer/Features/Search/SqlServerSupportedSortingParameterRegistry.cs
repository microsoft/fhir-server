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
    internal class SqlServerSupportedSortingParameterRegistry : ISupportedSortingParameterRegistry
    {
        private static readonly HashSet<string> _supportedParameterNames = new HashSet<string>(StringComparer.Ordinal)
        {
            KnownQueryParameterNames.LastUpdated, "birthdate", "date", "abatement-date", "onset-date", "issued", "created", "started", "authoredon",
        };

        public bool ValidateSortings(IReadOnlyList<(SearchParameterInfo searchParameter, SortOrder sortOrder)> sortings, out IReadOnlyList<string> errorMessages)
        {
            EnsureArg.IsNotNull(sortings, nameof(sortings));

            switch (sortings)
            {
                case { Count: 0 }:
                case { Count: 1 } when _supportedParameterNames.Contains(sortings[0].searchParameter.Name):
                    errorMessages = Array.Empty<string>();
                    return true;
                case { Count: 1 }:
                    errorMessages = new[] { string.Format(CultureInfo.InvariantCulture, Core.Resources.SearchSortParameterNotSupported, sortings[0].searchParameter.Name) };
                    return false;
                default:
                    errorMessages = new[] { Core.Resources.MultiSortParameterNotSupported };
                    return false;
            }
        }
    }
}
