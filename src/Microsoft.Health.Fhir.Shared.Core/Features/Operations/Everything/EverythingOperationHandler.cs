// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Everything;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Everything
{
    public class EverythingOperationHandler : IRequestHandler<EverythingOperationRequest, EverythingOperationResponse>
    {
        private readonly ISearchService _searchService;
        private readonly IBundleFactory _bundleFactory;
        private readonly IAuthorizationService<DataActions> _authorizationService;

        private IReadOnlyList<string> _includes = new[] { "general-practitioner", "organization" };
        private IReadOnlyList<Tuple<string, string>> _revincludes = new[] { Tuple.Create("Device", "patient") };

        public EverythingOperationHandler(ISearchService searchService, IBundleFactory bundleFactory, IAuthorizationService<DataActions> authorizationService)
        {
            EnsureArg.IsNotNull(searchService, nameof(searchService));
            EnsureArg.IsNotNull(bundleFactory, nameof(bundleFactory));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));

            _searchService = searchService;
            _bundleFactory = bundleFactory;
            _authorizationService = authorizationService;
        }

        public async Task<EverythingOperationResponse> Handle(EverythingOperationRequest message, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(message, nameof(message));

            if (await _authorizationService.CheckAccess(DataActions.Read, cancellationToken) != DataActions.Read)
            {
                throw new UnauthorizedFhirActionException();
            }

            SearchResult searchResult = await _searchService.SearchForEverythingOperationAsync(
                message.ResourceType, message.ResourceId, message.Start, message.End, message.Since, message.Type, message.Count, message.ContinuationToken, _includes, _revincludes, cancellationToken);

            ResourceElement bundle = _bundleFactory.CreateSearchBundle(searchResult);

            return new EverythingOperationResponse(bundle);
        }
    }
}
