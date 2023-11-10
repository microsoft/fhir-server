// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Messages.Search;

namespace Microsoft.Health.Fhir.Core.Features.Search.Registry
{
    public interface ISearchParameterStatusManager
    {
        Task AddSearchParameterStatusAsync(IReadOnlyCollection<string> searchParamUris, CancellationToken cancellationToken);
        Task ApplySearchParameterStatus(IReadOnlyCollection<ResourceSearchParameterStatus> updatedSearchParameterStatus, CancellationToken cancellationToken);
        Task DeleteSearchParameterStatusAsync(string url, CancellationToken cancellationToken);
        Task EnsureInitializedAsync(CancellationToken cancellationToken);
        Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetAllSearchParameterStatus(CancellationToken cancellationToken);
        Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetSearchParameterStatusUpdates(CancellationToken cancellationToken);
        Task Handle(SearchParameterDefinitionManagerInitialized notification, CancellationToken cancellationToken);
        Task UpdateSearchParameterStatusAsync(IReadOnlyCollection<string> searchParameterUris, SearchParameterStatus status, CancellationToken cancellationToken);
    }
}