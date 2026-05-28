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
    public interface ISearchParameterQueryParameterExpander
    {
        Task<IReadOnlyList<Tuple<string, string>>> ExpandAsync(
            string resourceType,
            IReadOnlyList<Tuple<string, string>> queryParameters,
            CancellationToken cancellationToken);
    }
}
