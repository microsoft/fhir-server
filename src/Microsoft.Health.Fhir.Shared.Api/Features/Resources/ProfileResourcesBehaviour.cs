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
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Features.Resources
{
    public sealed class ProfileResourcesBehaviour :
        IPipelineBehavior<CreateResourceRequest, UpsertResourceResponse>,
        IPipelineBehavior<UpsertResourceRequest, UpsertResourceResponse>,
        IPipelineBehavior<ConditionalCreateResourceRequest, UpsertResourceResponse>,
        IPipelineBehavior<ConditionalUpsertResourceRequest, UpsertResourceResponse>,
        IPipelineBehavior<DeleteResourceRequest, DeleteResourceResponse>
    {
        private IFhirAuthorizationService _authorizationService;
        private static IEnumerable<string> _supportedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ValueSet", "StructureDefinition", "CodeSystem", "ConceptMap" };

        public ProfileResourcesBehaviour(IFhirAuthorizationService authorizationService)
        {
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            _authorizationService = authorizationService;
        }

        public async Task<UpsertResourceResponse> Handle(ConditionalUpsertResourceRequest request, CancellationToken cancellationToken, RequestHandlerDelegate<UpsertResourceResponse> next)
            => await GenericHandle(request.Resource.InstanceType, next);

        public async Task<UpsertResourceResponse> Handle(ConditionalCreateResourceRequest request, CancellationToken cancellationToken, RequestHandlerDelegate<UpsertResourceResponse> next)
            => await GenericHandle(request.Resource.InstanceType, next);

        public async Task<UpsertResourceResponse> Handle(UpsertResourceRequest request, CancellationToken cancellationToken, RequestHandlerDelegate<UpsertResourceResponse> next)
            => await GenericHandle(request.Resource.InstanceType, next);

        public async Task<UpsertResourceResponse> Handle(CreateResourceRequest request, CancellationToken cancellationToken, RequestHandlerDelegate<UpsertResourceResponse> next)
            => await GenericHandle(request.Resource.InstanceType, next);

        public async Task<DeleteResourceResponse> Handle(DeleteResourceRequest request, CancellationToken cancellationToken, RequestHandlerDelegate<DeleteResourceResponse> next)
            => await GenericHandle(request.ResourceKey.ResourceType, next);

        private async Task<TResponse> GenericHandle<TResponse>(string resourceType, RequestHandlerDelegate<TResponse> next)
        {
            if (_supportedTypes.Contains(resourceType) && await _authorizationService.CheckAccess(DataActions.ProfileAdmin) != DataActions.ProfileAdmin)
            {
                throw new UnauthorizedFhirActionException();
            }

            return await next();
        }
    }
}
