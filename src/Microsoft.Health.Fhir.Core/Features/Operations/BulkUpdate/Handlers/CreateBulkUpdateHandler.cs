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
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate.Messages;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate.Handlers
{
    public class CreateBulkUpdateHandler : IRequestHandler<CreateBulkUpdateRequest, CreateBulkUpdateResponse>
    {
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly IQueueClient _queueClient;
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private readonly ISearchService _searchService;
        private readonly ILogger<CreateBulkUpdateHandler> _logger;
        private readonly List<string> _bulkUpdateSupportedOperations = new() { "Upsert", "Replace" };
        private readonly IResourceSerializer _resourceSerializer;

        public CreateBulkUpdateHandler(
            IAuthorizationService<DataActions> authorizationService,
            IQueueClient queueClient,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            ISearchService searchService,
            IResourceSerializer resourceSerializer,
            ILogger<CreateBulkUpdateHandler> logger)
        {
            _authorizationService = EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            _queueClient = EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            _contextAccessor = EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            _searchService = EnsureArg.IsNotNull(searchService, nameof(searchService));
            _resourceSerializer = EnsureArg.IsNotNull(resourceSerializer, nameof(resourceSerializer));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public async Task<CreateBulkUpdateResponse> Handle(CreateBulkUpdateRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            // Check access - Only super writer can perform bulk update
            if (await _authorizationService.CheckAccess(DataActions.BulkOperator, cancellationToken) != DataActions.BulkOperator)
            {
                throw new UnauthorizedFhirActionException();
            }

            // Should not run bulk Update if it is trying to update a resource types like SearchParameter and StructureDefinition
            if (OperationsConstants.ExcludedResourceTypesForBulkUpdate.Any(x => string.Equals(x, request.ResourceType, StringComparison.OrdinalIgnoreCase)))
            {
                throw new BadRequestException($"Bulk update is not supported for resource type {request.ResourceType}.");
            }

            var searchParameters = new List<Tuple<string, string>>(); // remove read only restriction
            if (request.ConditionalParameters != null && request.ConditionalParameters.Any())
            {
                // Add conditional parameters to searchParameters
                searchParameters.AddRange(request.ConditionalParameters);
            }

            // Remove _isParallel from searchParameters
            searchParameters = searchParameters.Where(x => !string.Equals(x.Item1, KnownQueryParameterNames.IsParallel, StringComparison.OrdinalIgnoreCase)).ToList();

            var dateCurrent = new PartialDateTime(Clock.UtcNow);
            searchParameters.Add(Tuple.Create("_lastUpdated", $"lt{dateCurrent}"));

            // remove maxCount from searchParameters
            searchParameters.RemoveAll(t => t.Item1.Equals(KnownQueryParameterNames.MaxCount, StringComparison.OrdinalIgnoreCase));

            // Should not run bulk Update if any of the search parameters are invalid as it can lead to unpredicatable results
            await _searchService.ConditionalSearchAsync(request.ResourceType, searchParameters, cancellationToken, count: 1, logger: _logger);
            if (_contextAccessor.RequestContext?.BundleIssues?.Count > 0 && _contextAccessor.RequestContext.BundleIssues.Any(x => !string.Equals(x.Diagnostics, Core.Resources.TruncatedIncludeMessageForIncludes, StringComparison.OrdinalIgnoreCase)))
            {
                throw new BadRequestException(_contextAccessor.RequestContext.BundleIssues.Select(issue => issue.Diagnostics).ToList());
            }

            // Validate that the operations are supported
            foreach (var parameter in request.Parameters.Parameter)
            {
                // Remove all parts except the type part
                Hl7.Fhir.Model.Parameters.ParameterComponent typePart = parameter.Part.FirstOrDefault(p => p.Name.Equals("type", StringComparison.OrdinalIgnoreCase));
                var operationValue = typePart.Value.Select(x => x.Value).First().ToString();
                if (!_bulkUpdateSupportedOperations.Any(op => op.Equals(operationValue, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new BadRequestException("Bulk update only supports Replace or Upsert operation types");
                }
            }

            // converting parameters to string using fhir Serializer
            var parametersString = _resourceSerializer.Serialize(request.Parameters, FhirResourceFormat.Json);
            var processingDefinition = new BulkUpdateDefinition(
                JobType.BulkUpdateOrchestrator,
                request.ResourceType,
                searchParameters,
                _contextAccessor.RequestContext.Uri.ToString(),
                _contextAccessor.RequestContext.BaseUri.ToString(),
                _contextAccessor.RequestContext.CorrelationId,
                parametersString,
                request.IsParallel,
                maximumNumberOfResourcesPerQuery: request.MaxCount);

            IReadOnlyList<JobInfo> jobInfo;
            try
            {
                jobInfo = await _queueClient.EnqueueAsync(QueueType.BulkUpdate, cancellationToken, forceOneActiveJobGroup: true, definitions: processingDefinition);
            }
            catch (JobManagement.JobConflictException ex) when (ex.Message.Equals("There are other active job groups", StringComparison.Ordinal))
            {
                throw new BadRequestException("A bulk update job is already running.");
            }

            if (jobInfo == null || jobInfo.Count == 0)
            {
                throw new JobNotExistException("Failed to create bulk update job");
            }

            return new CreateBulkUpdateResponse(jobInfo[0].Id);
        }
    }
}
