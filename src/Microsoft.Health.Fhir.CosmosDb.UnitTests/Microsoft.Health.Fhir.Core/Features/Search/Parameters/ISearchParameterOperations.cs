// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Features.Search.Parameters
{
    public interface ISearchParameterOperations
    {
        Task AddSearchParameterAsync(ITypedElement searchParam);

        Task DeleteSearchParameterAsync(RawResource searchParamResource);

        Task UpdateSearchParameterAsync(ITypedElement searchParam, RawResource previousSearchParam);

        /// <summary>
        /// This method should be called periodically to get any updates to SearchParameters
        /// added to the DB by other service instances.
        /// It should also be called when a user starts a reindex job
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A task.</returns>
        Task GetAndApplySearchParameterUpdates(CancellationToken cancellationToken);
    }
}
