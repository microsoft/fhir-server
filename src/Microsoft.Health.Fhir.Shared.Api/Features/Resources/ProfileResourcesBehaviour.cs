// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Messages.Upsert;

namespace Microsoft.Health.Fhir.Api.Features.Resources
{
    public sealed class ProfileResourcesBehaviour :
        IPipelineBehavior<CreateResourceRequest, UpsertResourceResponse>,
        IPipelineBehavior<UpsertResourceRequest, UpsertResourceResponse>,
        IPipelineBehavior<ConditionalCreateResourceRequest, UpsertResourceResponse>,
        IPipelineBehavior<ConditionalUpsertResourceRequest, UpsertResourceResponse>,
        IPipelineBehavior<DeleteResourceRequest, DeleteResourceResponse>
    {
        private IAuthorizationService<DataActions> _authorizationService;
        private IProvideProfilesForValidation _profilesResolver;

        public ProfileResourcesBehaviour(IAuthorizationService<DataActions> authorizationService, IProvideProfilesForValidation profilesResolver)
        {
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(profilesResolver, nameof(profilesResolver));

            _authorizationService = authorizationService;
            _profilesResolver = profilesResolver;
        }

        public async Task<UpsertResourceResponse> Handle(ConditionalUpsertResourceRequest request, RequestHandlerDelegate<UpsertResourceResponse> next, CancellationToken cancellationToken)
            => await GenericHandle(request.Resource.InstanceType, next, cancellationToken);

        public async Task<UpsertResourceResponse> Handle(ConditionalCreateResourceRequest request, RequestHandlerDelegate<UpsertResourceResponse> next, CancellationToken cancellationToken)
            => await GenericHandle(request.Resource.InstanceType, next, cancellationToken);

        public async Task<UpsertResourceResponse> Handle(UpsertResourceRequest request, RequestHandlerDelegate<UpsertResourceResponse> next, CancellationToken cancellationToken)
            => await GenericHandle(request.Resource.InstanceType, next, cancellationToken);

        public async Task<UpsertResourceResponse> Handle(CreateResourceRequest request, RequestHandlerDelegate<UpsertResourceResponse> next, CancellationToken cancellationToken)
            => await GenericHandle(request.Resource.InstanceType, next, cancellationToken);

        public async Task<DeleteResourceResponse> Handle(DeleteResourceRequest request, RequestHandlerDelegate<DeleteResourceResponse> next, CancellationToken cancellationToken)
            => await GenericHandle(request.ResourceKey.ResourceType, next, cancellationToken);

        private async Task<TResponse> GenericHandle<TResponse>(string resourceType, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            var resources = _profilesResolver.GetProfilesTypes();
            if (resources.Contains(resourceType))
            {
                if (await _authorizationService.CheckAccess(DataActions.EditProfileDefinitions, cancellationToken) != DataActions.EditProfileDefinitions)
                {
                    throw new UnauthorizedFhirActionException();
                }

                var result = await next();
                _profilesResolver.Refresh();
                return result;
            }
            else
            {
                return await next();
            }
        }
    }
}
