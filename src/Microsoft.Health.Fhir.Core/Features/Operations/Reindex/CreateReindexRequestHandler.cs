// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Reindex;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    public class CreateReindexRequestHandler : IRequestHandler<CreateReindexRequest, CreateReindexResponse>
    {
        private readonly IFhirOperationDataStore _fhirOperationDataStore;
        private readonly IFhirAuthorizationService _authorizationService;
        private readonly ReindexJobConfiguration _reindexJobConfiguration;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;

        public CreateReindexRequestHandler(
            IFhirOperationDataStore fhirOperationDataStore,
            IFhirAuthorizationService authorizationService,
            IOptions<ReindexJobConfiguration> reindexJobConfiguration,
            ISearchParameterDefinitionManager searchParameterDefinitionManager)
        {
            EnsureArg.IsNotNull(fhirOperationDataStore, nameof(fhirOperationDataStore));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(reindexJobConfiguration, nameof(reindexJobConfiguration));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));

            _fhirOperationDataStore = fhirOperationDataStore;
            _authorizationService = authorizationService;
            _reindexJobConfiguration = reindexJobConfiguration.Value;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
        }

        public async Task<CreateReindexResponse> Handle(CreateReindexRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Reindex) != DataActions.Reindex)
            {
                throw new UnauthorizedFhirActionException();
            }

            (var activeReindexJobs, var reindexJobId) = await _fhirOperationDataStore.CheckActiveReindexJobsAsync(cancellationToken);
            if (activeReindexJobs)
            {
                throw new JobConflictException(string.Format(Resources.OnlyOneResourceJobAllowed, reindexJobId));
            }

            var jobRecord = new ReindexJobRecord(
                _searchParameterDefinitionManager.SearchParameterHashMap,
                request.MaximumConcurrency ?? _reindexJobConfiguration.DefaultMaximumThreadsPerReindexJob,
                _reindexJobConfiguration.MaximumNumberOfResourcesPerQuery);
            var outcome = await _fhirOperationDataStore.CreateReindexJobAsync(jobRecord, cancellationToken);

            return new CreateReindexResponse(outcome);
        }
    }
}
