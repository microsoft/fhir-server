// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public interface ISearchOptionsFactory
    {
        Task<SearchOptions> Create(string resourceType, IReadOnlyList<Tuple<string, string>> queryParameters, bool isAsyncOperation = false, CancellationToken cancellationToken = default);

        Task<SearchOptions> Create(string compartmentType, string compartmentId, string resourceType, IReadOnlyList<Tuple<string, string>> queryParameters, bool isAsyncOperation = false, bool useSmartCompartmentDefinition = false, CancellationToken cancellationToken = default);
    }
}
