// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Messages.Search;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    /// <summary>
    /// A status manager replacement that throws when handling
    /// SearchParameterDefinitionManagerInitialized, simulating a status-stage failure.
    /// Other ISearchParameterStatusManager methods are no-ops.
    /// </summary>
    public class FailingSearchParameterStatusManager : ISearchParameterStatusManager
    {
        public Task Handle(SearchParameterDefinitionManagerInitialized notification, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Simulated status-stage search parameter initialization failure.");

        public Task AddSearchParameterStatusAsync(IReadOnlyCollection<string> searchParamUris, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ApplySearchParameterStatus(IReadOnlyCollection<ResourceSearchParameterStatus> updatedSearchParameterStatus, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteSearchParameterStatusAsync(string url, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetAllSearchParameterStatus(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<ResourceSearchParameterStatus>>(Array.Empty<ResourceSearchParameterStatus>());

        public Task UpdateSearchParameterStatusAsync(IReadOnlyCollection<string> searchParameterUris, SearchParameterStatus status, CancellationToken cancellationToken, bool ignoreSearchParameterNotSupportedException = false) => Task.CompletedTask;
    }
}
