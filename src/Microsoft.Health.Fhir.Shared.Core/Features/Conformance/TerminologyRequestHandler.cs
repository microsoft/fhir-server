// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Medino;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Conformance;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Shared.Core.Features.Conformance
{
    public class TerminologyRequestHandler : IRequestHandler<ExpandRequest, ExpandResponse>
    {
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly ITerminologyServiceProxy _terminologyServiceProxy;
        private readonly IMediator _mediator;
        private readonly ILogger<TerminologyRequestHandler> _logger;

        public TerminologyRequestHandler(
            IAuthorizationService<DataActions> authorizationService,
            ITerminologyServiceProxy terminologyServiceProxy,
            IMediator mediator,
            ILogger<TerminologyRequestHandler> logger)
        {
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(terminologyServiceProxy, nameof(terminologyServiceProxy));
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _authorizationService = authorizationService;
            _terminologyServiceProxy = terminologyServiceProxy;
            _mediator = mediator;
            _logger = logger;
        }

        public async Task<ExpandResponse> HandleAsync(
            ExpandRequest request,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Read, cancellationToken) != DataActions.Read)
            {
                throw new UnauthorizedFhirActionException();
            }

            try
            {
                var parameters = request.Parameters.ToList();
                if (!string.IsNullOrEmpty(request.ResourceId))
                {
                    try
                    {
                        var rawResource = await _mediator.GetResourceAsync(
                            new ResourceKey(KnownResourceTypes.ValueSet, request.ResourceId),
                            cancellationToken);
                        parameters.Add(Tuple.Create(TerminologyOperationParameterNames.Expand.ValueSet, rawResource?.RawResource?.Data));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to get a resource with the id: '{Id}'.", request.ResourceId);
                        throw;
                    }
                }

                var resource = await _terminologyServiceProxy.ExpandAsync(
                    parameters,
                    request.ResourceId,
                    cancellationToken);
                return new ExpandResponse(resource);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to handle the request.");
                throw;
            }
        }
    }
}
