// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Create
{
    public class ConditionalCreateResourceHandler : BaseResourceHandler, IRequestHandler<ConditionalCreateResourceRequest, UpsertResourceResponse>
    {
        private readonly ISearchService _searchService;
        private readonly IMediator _mediator;
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly bool _featureEnabled;

        public ConditionalCreateResourceHandler(
            IFhirDataStore fhirDataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory,
            ISearchService searchService,
            IMediator mediator,
            IsEnabled featureEnabled,
            IModelInfoProvider modelInfoProvider)
            : base(fhirDataStore, conformanceProvider, resourceWrapperFactory)
        {
            EnsureArg.IsNotNull(searchService, nameof(searchService));
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(featureEnabled, nameof(featureEnabled));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));

            _searchService = searchService;
            _mediator = mediator;
            _modelInfoProvider = modelInfoProvider;
            _featureEnabled = featureEnabled();
        }

        public delegate bool IsEnabled();

        public async Task<UpsertResourceResponse> Handle(ConditionalCreateResourceRequest message, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(message, nameof(message));

            SearchResult results = await _searchService.SearchAsync(message.Resource.InstanceType, message.ConditionalParameters, cancellationToken);

            int count = results.Results.Count();
            if (count == 0)
            {
                // No matches: The server creates the resource
                // TODO: There is a potential contention issue here in that this could create another new resource with a different id
                return await _mediator.Send<UpsertResourceResponse>(new CreateResourceRequest(message.Resource), cancellationToken);
            }
            else if (count == 1)
            {
                return null;
            }
            else
            {
                // Multiple matches: The server returns a 412 Precondition Failed error indicating the client's criteria were not selective enough
                throw new PreconditionFailedException(Core.Resources.ConditionalOperationNotSelectiveEnough);
            }
        }

        public void Build(ICapabilityStatementBuilder builder)
        {
            if (_featureEnabled)
            {
                foreach (var resource in _modelInfoProvider.GetResourceTypeNames())
                {
                    builder.UpdateRestResourceComponent(resource, c => c.ConditionalCreate = true);
                }
            }
        }
    }
}
