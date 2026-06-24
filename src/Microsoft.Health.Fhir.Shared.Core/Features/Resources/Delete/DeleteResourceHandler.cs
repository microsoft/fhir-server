// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Delete;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Delete
{
    public class DeleteResourceHandler : BaseResourceHandler, IRequestHandler<DeleteResourceRequest, DeleteResourceResponse>
    {
        private readonly IDeletionService _deleter;
        private readonly ISearchService _searchService;
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;

        public DeleteResourceHandler(
            IFhirDataStore fhirDataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory,
            ResourceIdProvider resourceIdProvider,
            IAuthorizationService<DataActions> authorizationService,
            IDeletionService deleter,
            ISearchService searchService,
            RequestContextAccessor<IFhirRequestContext> contextAccessor)
            : base(fhirDataStore, conformanceProvider, resourceWrapperFactory, resourceIdProvider, authorizationService)
        {
            _deleter = EnsureArg.IsNotNull(deleter, nameof(deleter));
            _searchService = EnsureArg.IsNotNull(searchService, nameof(searchService));
            _contextAccessor = EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
        }

        public async Task<DeleteResourceResponse> Handle(DeleteResourceRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            await AuthorizationService.CheckDeleteAccess(
                cancellationToken,
                request.DeleteOperation != DeleteOperation.SoftDelete);

            // For SMART fine-grained access control, ensure the targeted resource belongs to the caller's
            // compartment before deleting it. Otherwise a patient-scoped token could delete another patient's data.
            await SmartCompartmentResourceValidator.EnsureResourceIsInCompartmentAsync(
                _searchService,
                _contextAccessor,
                request.ResourceKey?.ResourceType,
                request.ResourceKey?.Id,
                cancellationToken);

            var result = await _deleter.DeleteAsync(request, cancellationToken);

            if (string.IsNullOrWhiteSpace(result.VersionId))
            {
                return new DeleteResourceResponse(result);
            }

            return new DeleteResourceResponse(result, weakETag: WeakETag.FromVersionId(result.VersionId));
        }
    }
}
