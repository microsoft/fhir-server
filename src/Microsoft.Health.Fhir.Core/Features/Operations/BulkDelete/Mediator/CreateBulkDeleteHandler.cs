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
using Microsoft.Health.Core;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete.Mediator
{
    public class CreateBulkDeleteHandler : IRequestHandler<CreateBulkDeleteRequest, CreateBulkDeleteResponse>
    {
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly IQueueClient _queueClient;
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private readonly ISearchService _searchService;

        public CreateBulkDeleteHandler(
            IAuthorizationService<DataActions> authorizationService,
            IQueueClient queueClient,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            ISearchService searchService)
        {
            _authorizationService = EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            _queueClient = EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            _contextAccessor = EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            _searchService = EnsureArg.IsNotNull(searchService, nameof(searchService));
        }

        public async Task<CreateBulkDeleteResponse> Handle(CreateBulkDeleteRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            DataActions requiredDataAction = request.DeleteOperation == DeleteOperation.SoftDelete ? DataActions.Delete : DataActions.HardDelete | DataActions.Delete;
            if (await _authorizationService.CheckAccess(requiredDataAction, cancellationToken) != requiredDataAction)
            {
                throw new UnauthorizedFhirActionException();
            }

            var searchParameters = new List<Tuple<string, string>>(request.ConditionalParameters); // remove read only restriction
            var dateCurrent = new PartialDateTime(Clock.UtcNow);
            searchParameters.Add(Tuple.Create("_lastUpdated", $"lt{dateCurrent}"));

            // Should not run bulk delete if any of the search parameters are invalid as it can lead to unpredicatable results
            await _searchService.ConditionalSearchAsync(request.ResourceType, searchParameters, 1, cancellationToken);
            if (_contextAccessor.RequestContext?.BundleIssues?.Count > 0)
            {
                throw new BadRequestException(_contextAccessor.RequestContext.BundleIssues.Select(issue => issue.Diagnostics).ToList());
            }

            var definitions = new List<string>();
            var processingDefinition = new BulkDeleteDefinition(JobType.BulkDeleteOrchestrator, request.DeleteOperation, request.ResourceType, searchParameters, _contextAccessor.RequestContext.Uri.ToString(), _contextAccessor.RequestContext.BaseUri.ToString());
            definitions.Add(JsonConvert.SerializeObject(processingDefinition));
            var jobInfo = await _queueClient.EnqueueAsync((byte)QueueType.BulkDelete, definitions.ToArray(), null, false, false, cancellationToken);

            if (jobInfo == null || jobInfo.Count == 0)
            {
                throw new JobNotExistException("Failed to create job");
            }

            return new CreateBulkDeleteResponse(jobInfo[0].Id);
        }
    }
}
