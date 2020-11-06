// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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

        public bool IsSortSupported(SearchParameterInfo searchParameterInfo)
        {
            EnsureArg.IsNotNull(searchParameterInfo, nameof(searchParameterInfo));

            return _supportedParameterNames.Contains(searchParameterInfo.Name);
        }
    }
}
