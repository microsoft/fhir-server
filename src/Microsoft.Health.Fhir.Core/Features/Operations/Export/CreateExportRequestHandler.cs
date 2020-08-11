// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Export;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public class CreateExportRequestHandler : IRequestHandler<CreateExportRequest, CreateExportResponse>
    {
        private readonly IClaimsExtractor _claimsExtractor;
        private readonly IFhirOperationDataStore _fhirOperationDataStore;
        private readonly IFhirAuthorizationService _authorizationService;
        private readonly ExportJobConfiguration _exportJobConfiguration;

        public CreateExportRequestHandler(
            IClaimsExtractor claimsExtractor,
            IFhirOperationDataStore fhirOperationDataStore,
            IFhirAuthorizationService authorizationService,
            IOptions<ExportJobConfiguration> exportJobConfiguration)
        {
            EnsureArg.IsNotNull(claimsExtractor, nameof(claimsExtractor));
            EnsureArg.IsNotNull(fhirOperationDataStore, nameof(fhirOperationDataStore));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(exportJobConfiguration?.Value, nameof(exportJobConfiguration));

            _claimsExtractor = claimsExtractor;
            _fhirOperationDataStore = fhirOperationDataStore;
            _authorizationService = authorizationService;
            _exportJobConfiguration = exportJobConfiguration.Value;
        }

        public async Task<CreateExportResponse> Handle(CreateExportRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Export) != DataActions.Export)
            {
                throw new UnauthorizedFhirActionException();
            }

            IReadOnlyCollection<KeyValuePair<string, string>> requestorClaims = _claimsExtractor.Extract()?
                .OrderBy(claim => claim.Key, StringComparer.Ordinal).ToList();

            // Compute the hash of the job.
            var hashObject = new
            {
                request.RequestUri,
                RequestorClaims = requestorClaims,
            };

            string hash = JsonConvert.SerializeObject(hashObject).ComputeHash();

            string storageAccountConnectionHash = string.IsNullOrEmpty(_exportJobConfiguration.StorageAccountConnection) ?
                string.Empty :
                Microsoft.Health.Core.Extensions.StringExtensions.ComputeHash(_exportJobConfiguration.StorageAccountConnection);

            // Check to see if a matching job exists or not. If a matching job exists, we will return that instead.
            // Otherwise, we will create a new export job. This will be a best effort since the likelihood of this happen should be small.
            ExportJobOutcome outcome = await _fhirOperationDataStore.GetExportJobByHashAsync(hash, cancellationToken);

            if (outcome == null)
            {
                var jobRecord = new ExportJobRecord(
                    request.RequestUri,
                    request.RequestType,
                    request.ResourceType,
                    hash,
                    requestorClaims,
                    request.Since,
                    request.GroupId,
                    storageAccountConnectionHash,
                    _exportJobConfiguration.StorageAccountUri,
                    request.AnonymizationConfigurationLocation,
                    request.AnonymizationConfigurationFileETag,
                    _exportJobConfiguration.MaximumNumberOfResourcesPerQuery,
                    _exportJobConfiguration.NumberOfPagesPerCommit);

                outcome = await _fhirOperationDataStore.CreateExportJobAsync(jobRecord, cancellationToken);
            }

            return new CreateExportResponse(outcome.JobRecord.Id);
        }
    }
}
