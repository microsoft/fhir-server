// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Reindex;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    public class CreateReindexRequestHandler : IRequestHandler<CreateReindexRequest, CreateReindexResponse>
    {
        private readonly IFhirOperationDataStore _fhirOperationDataStore;
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly ReindexJobConfiguration _reindexJobConfiguration;

        public CreateReindexRequestHandler(
            IFhirOperationDataStore fhirOperationDataStore,
            IAuthorizationService<DataActions> authorizationService,
            IOptions<ReindexJobConfiguration> reindexJobConfiguration)
        {
            EnsureArg.IsNotNull(fhirOperationDataStore, nameof(fhirOperationDataStore));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(reindexJobConfiguration, nameof(reindexJobConfiguration));

            _fhirOperationDataStore = fhirOperationDataStore;
            _authorizationService = authorizationService;
            _reindexJobConfiguration = reindexJobConfiguration.Value;
        }

        public async Task<CreateReindexResponse> Handle(CreateReindexRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            await _authorizationService.CheckAccess(DataActions.Reindex, true, cancellationToken);

            // Check for active reindex jobs - but don't block, instead signal for updates
            (var activeReindexJobs, var reindexJobId) = await _fhirOperationDataStore.CheckActiveReindexJobsAsync(cancellationToken);

            if (activeReindexJobs)
            {
                // Return the existing job information instead of creating a new one
                var existingJob = await _fhirOperationDataStore.GetReindexJobByIdAsync(reindexJobId, cancellationToken);
                return new CreateReindexResponse(existingJob);
            }

            var jobRecord = new ReindexJobRecord(
            request.TargetResourceTypes,
            request.MaximumResourcesPerQuery ?? _reindexJobConfiguration.MaximumNumberOfResourcesPerQuery,
            request.MaximumResourcesPerWrite ?? _reindexJobConfiguration.MaximumNumberOfResourcesPerWrite,
            request.QueryDelayIntervalInMilliseconds ?? _reindexJobConfiguration.QueryDelayIntervalInMilliseconds);

            var outcome = await _fhirOperationDataStore.CreateReindexJobAsync(jobRecord, cancellationToken);

            return new CreateReindexResponse(outcome);
        }
    }
}
