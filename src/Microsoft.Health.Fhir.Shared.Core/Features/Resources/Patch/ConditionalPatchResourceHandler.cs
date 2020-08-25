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
using Hl7.Fhir.Patch;
using MediatR;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Patch;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Patch
{
    /// <summary>
    /// Handles Conditional Patch logic as defined in the spec https://www.hl7.org/fhir/http.html#patch
    /// </summary>
    public class ConditionalPatchResourceHandler
        : BasePatchResourceHandler, IRequestHandler<ConditionalPatchResourceRequest, PatchResourceResponse>
    {
        private readonly ISearchService _searchService;

        public ConditionalPatchResourceHandler(
            IFhirDataStore fhirDataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory,
            ISearchService searchService,
            ResourceIdProvider resourceIdProvider,
            IFhirAuthorizationService authorizationService,
            IModelInfoProvider modelInfoProvider,
            ResourceDeserializer resourceDeserializer)
            : base(fhirDataStore, conformanceProvider, resourceWrapperFactory, resourceIdProvider, authorizationService, modelInfoProvider, resourceDeserializer)
        {
            EnsureArg.IsNotNull(searchService, nameof(searchService));

            _searchService = searchService;
        }

        public async Task<PatchResourceResponse> Handle(ConditionalPatchResourceRequest message, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(message, nameof(message));

            if (await AuthorizationService.CheckAccess(DataActions.Read | DataActions.Write) != (DataActions.Read | DataActions.Write))
            {
                throw new UnauthorizedFhirActionException();
            }

            IReadOnlyCollection<SearchResultEntry> matchedResults = await _searchService.ConditionalSearchAsync(message.ResourceType, message.ConditionalParameters, cancellationToken);

            int count = matchedResults.Count;
            if (count == 0)
            {
                // No matches: The server returns a 404 Not Found error indicating the client's criteria did not match any resources
                throw new ResourceNotFoundException(Core.Resources.ConditionalOperationNotMatchedAndResource);
            }
            else if (count == 1)
            {
                ResourceWrapper currentDoc = matchedResults.First().Resource;
                var version = WeakETag.FromVersionId(currentDoc.Version);
                return await ApplyPatch(currentDoc, message.PatchDocument, version, cancellationToken);
            }
            else
            {
                // Multiple matches: The server returns a 412 Precondition Failed error indicating the client's criteria were not selective enough
                throw new PreconditionFailedException(Core.Resources.ConditionalOperationNotSelectiveEnough);
            }
        }
    }
}
