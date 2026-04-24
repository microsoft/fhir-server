// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    /// <summary>
    /// A search parameter status data store stub that always throws, simulating a
    /// definition-stage initialization failure.
    /// </summary>
    public class AlwaysFailingSearchParameterStatusDataStore : ISearchParameterStatusDataStore
    {
        public Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetSearchParameterStatuses(CancellationToken cancellationToken, DateTimeOffset? startLastUpdated = null)
            => throw new InvalidOperationException("Simulated definition-stage search parameter initialization failure.");

        public Task UpsertStatuses(IReadOnlyCollection<ResourceSearchParameterStatus> statuses, CancellationToken cancellationToken) => Task.CompletedTask;

        public void SyncStatuses(IReadOnlyCollection<ResourceSearchParameterStatus> statuses)
        {
        }

        public Task TryLogEvent(string process, string status, string text, DateTime? startDate, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
