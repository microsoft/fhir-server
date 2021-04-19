// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Patch;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Patch
{
    public class PatchResourceHandler : BaseResourceHandler, IRequestHandler<PatchResourceRequest, PatchResourceResponse>
    {
        private readonly IModelInfoProvider _modelInfoProvider;

        public PatchResourceHandler(
            IFhirDataStore fhirDataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory,
            ResourceIdProvider resourceIdProvider,
            IAuthorizationService<DataActions> authorizationService,
            IModelInfoProvider modelInfoProvider)
            : base(fhirDataStore, conformanceProvider, resourceWrapperFactory, resourceIdProvider, authorizationService)
        {
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));

            _modelInfoProvider = modelInfoProvider;
        }

        public async Task<PatchResourceResponse> Handle(PatchResourceRequest message, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(message, nameof(message));

            if (await AuthorizationService.CheckAccess(DataActions.Write, cancellationToken) != DataActions.Write)
            {
                throw new UnauthorizedFhirActionException();
            }

            var key = message.ResourceKey;

            if (!string.IsNullOrEmpty(key.VersionId))
            {
                throw new MethodNotAllowedException(Core.Resources.DeleteVersionNotAllowed);
            }

            /*
                bool allowCreate = await ConformanceProvider.Value.CanUpdateCreate(resource.TypeName, cancellationToken);
                bool keepHistory = await ConformanceProvider.Value.CanKeepHistory(resource.TypeName, cancellationToken);
                ResourceWrapper resourceWrapper = CreateResourceWrapper(resource, deleted: false, keepMeta: allowCreate);
            */

            string version = null;

            if (message.PatchDocument != null)
            {
                // To-do: Patch document operation
                // Call the data store Patch operation
                Console.WriteLine("Patch operation placeholder");
                /*  UpsertOutcome result = await PatchAsync(message, resourceWrapper, allowCreate, keepHistory, cancellationToken);*/
            }

            if (string.IsNullOrWhiteSpace(version))
            {
                return new PatchResourceResponse(new ResourceKey(key.ResourceType, key.Id));
            }

            return new PatchResourceResponse(new ResourceKey(key.ResourceType, key.Id, version), WeakETag.FromVersionId(version));
        }

        /* To-do update with patch logic
        private async Task<UpsertOutcome> PatchAsync(PatchResourceRequest message, ResourceWrapper resourceWrapper, bool allowCreate, bool keepHistory, CancellationToken cancellationToken)
        {
            UpsertOutcome result = null;

            try
            {
                  UpsertOutcome result = await FhirDataStore.PatchAsync(
                    deletedWrapper,
                    weakETag: null,
                    allowCreate: true,
                    keepHistory: keepHistory,
                    cancellationToken: cancellationToken);
                // result = await FhirDataStore.UpsertAsync(resourceWrapper, message.WeakETag, allowCreate, keepHistory, cancellationToken);
            }
            catch (PreconditionFailedException) when (_modelInfoProvider.Version == FhirSpecification.Stu3)
            {
                // The backwards compatibility behavior of Stu3 is to return a Conflict instead of Precondition fail
                throw new ResourceConflictException(message.WeakETag);
            }

            return result;
        }
        */
    }
}
