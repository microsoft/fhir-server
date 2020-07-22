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
        private readonly IClaimsExtractor _claimsExtractor;
        private readonly IFhirOperationDataStore _fhirOperationDataStore;
        private readonly IFhirAuthorizationService _authorizationService;
        private readonly ReindexJobConfiguration _reindexJobConfiguration;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;

        public CreateReindexRequestHandler(
            IClaimsExtractor claimsExtractor,
            IFhirOperationDataStore fhirOperationDataStore,
            IFhirAuthorizationService authorizationService,
            IOptions<ReindexJobConfiguration> reindexJobConfiguration,
            ISearchParameterDefinitionManager searchParameterDefinitionManager)
        {
            EnsureArg.IsNotNull(claimsExtractor, nameof(claimsExtractor));
            EnsureArg.IsNotNull(fhirOperationDataStore, nameof(fhirOperationDataStore));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(reindexJobConfiguration, nameof(reindexJobConfiguration));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));

            _claimsExtractor = claimsExtractor;
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

            if (await _fhirOperationDataStore.CheckActiveReindexJobsAsync(cancellationToken))
            {
                throw new JobConflictException(Resources.OnlyOneResourceJobAllowed);
            }

            // TODO: this is a placeholder until we determine how to best store and communicate
            // hash value
            var hash = _searchParameterDefinitionManager.SearchParametersHash ?? "hash";

            var jobRecord = new ReindexJobRecord(
                hash,
                request.MaximumConcurrency ?? _reindexJobConfiguration.DefaultMaximumThreadsPerReindexJob,
                request.Scope);
            var outcome = await _fhirOperationDataStore.CreateReindexJobAsync(jobRecord, cancellationToken);

            return new CreateReindexResponse(outcome);
        }
    }
}
