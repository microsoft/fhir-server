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
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Reindex;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    public class CreateReindexRequestHandler : IRequestHandler<CreateReindexRequest, CreateReindexResponse>
    {
        private readonly IFhirOperationDataStore _fhirOperationDataStore;
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly ReindexJobConfiguration _reindexJobConfiguration;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ISearchParameterOperations _searchParameterOperations;

        public CreateReindexRequestHandler(
            IFhirOperationDataStore fhirOperationDataStore,
            IAuthorizationService<DataActions> authorizationService,
            IOptions<ReindexJobConfiguration> reindexJobConfiguration,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            ISearchParameterOperations searchParameterOperations)
        {
            EnsureArg.IsNotNull(fhirOperationDataStore, nameof(fhirOperationDataStore));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(reindexJobConfiguration, nameof(reindexJobConfiguration));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(searchParameterOperations, nameof(searchParameterOperations));

            _fhirOperationDataStore = fhirOperationDataStore;
            _authorizationService = authorizationService;
            _reindexJobConfiguration = reindexJobConfiguration.Value;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _searchParameterOperations = searchParameterOperations;
        }

        public async Task<CreateReindexResponse> Handle(CreateReindexRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Reindex, cancellationToken) != DataActions.Reindex)
            {
                throw new UnauthorizedFhirActionException();
            }

            (var activeReindexJobs, var reindexJobId) = await _fhirOperationDataStore.CheckActiveReindexJobsAsync(cancellationToken);
            if (activeReindexJobs)
            {
                throw new JobConflictException(string.Format(Resources.OnlyOneResourceJobAllowed, reindexJobId));
            }

            // We need to pull in latest search parameter updates from the data store before creating a reindex job.
            // There could be a potential delay of <see cref="ReindexJobConfiguration.JobPollingFrequency"/> before
            // search parameter updates on one instance propagates to other instances. If we store the reindex
            // job with the old hash value in _searchParameterDefinitionManager.SearchParameterHashMap, then we will
            // not detect the resources that need to be reindexed.
            await _searchParameterOperations.GetAndApplySearchParameterUpdates(cancellationToken);

            var jobRecord = new ReindexJobRecord(
                _searchParameterDefinitionManager.SearchParameterHashMap,
                request.MaximumConcurrency ?? _reindexJobConfiguration.DefaultMaximumThreadsPerReindexJob,
                request.MaximumResourcesPerQuery ?? _reindexJobConfiguration.MaximumNumberOfResourcesPerQuery,
                request.QueryDelayIntervalInMilliseconds ?? _reindexJobConfiguration.QueryDelayIntervalInMilliseconds,
                request.TargetDataStoreUsagePercentage);
            var outcome = await _fhirOperationDataStore.CreateReindexJobAsync(jobRecord, cancellationToken);

            return new CreateReindexResponse(outcome);
        }
    }
}
