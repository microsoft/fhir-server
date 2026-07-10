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
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Search;

namespace Microsoft.Health.Fhir.Core.Features.Search.Behavior
{
    /// <summary>
    /// Authorizes list searches before resolving the referenced list.
    /// </summary>
    public sealed class ListSearchAuthorizationPipeBehavior : IPipelineBehavior<SearchResourceRequest, SearchResourceResponse>
    {
        private readonly IAuthorizationService<DataActions> _authorizationService;

        /// <summary>
        /// Initializes a new instance of the <see cref="ListSearchAuthorizationPipeBehavior"/> class.
        /// </summary>
        /// <param name="authorizationService">The FHIR data action authorization service.</param>
        public ListSearchAuthorizationPipeBehavior(IAuthorizationService<DataActions> authorizationService)
        {
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            _authorizationService = authorizationService;
        }

        /// <inheritdoc />
        public async Task<SearchResourceResponse> HandleAsync(
            SearchResourceRequest request,
            RequestHandlerDelegate<SearchResourceResponse> next,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            Tuple<string, string> listParameter = request.Queries
                .FirstOrDefault(x => string.Equals(x.Item1, KnownQueryParameterNames.List, StringComparison.OrdinalIgnoreCase));

            if (listParameter != null && !string.IsNullOrWhiteSpace(listParameter.Item2))
            {
                await _authorizationService.CheckAccess(
                    DataActions.Search | DataActions.Read,
                    x => (x & (DataActions.Search | DataActions.Read)) != DataActions.None,
                    true,
                    cancellationToken);
            }

            return await next();
        }
    }
}
