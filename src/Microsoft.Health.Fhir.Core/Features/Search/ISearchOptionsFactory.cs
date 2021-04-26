// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public interface ISearchOptionsFactory
    {
        SearchOptions Create(string resourceType, IReadOnlyList<Tuple<string, string>> queryParameters);

        SearchOptions Create(string compartmentType, string compartmentId, string resourceType, IReadOnlyList<Tuple<string, string>> queryParameters);

        SearchOptions Create(string compartmentType, string compartmentId, PartialDateTime start, PartialDateTime end, PartialDateTime since, string type, int? count, string continuationToken);
    }
}
