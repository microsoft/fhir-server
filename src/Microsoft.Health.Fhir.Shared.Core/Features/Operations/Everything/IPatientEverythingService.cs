// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Everything
{
    public interface IPatientEverythingService
    {
        Task<SearchResult> SearchAsync(
            string resourceId,
            PartialDateTime start,
            PartialDateTime end,
            PartialDateTime since,
            string type,
            string continuationToken,
            CancellationToken cancellationToken);
    }
}
