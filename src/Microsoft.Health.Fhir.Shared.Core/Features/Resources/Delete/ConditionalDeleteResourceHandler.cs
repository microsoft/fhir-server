// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Medino;
using Microsoft.Build.Framework;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Delete;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Delete
{
    public class ConditionalDeleteResourceHandler : BaseResourceHandler, IRequestHandler<ConditionalDeleteResourceRequest, DeleteResourceResponse>
    {
        private readonly ISearchService _searchService;
        private readonly IDeletionService _deleter;
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirContext;
        private readonly CoreFeatureConfiguration _configuration;
        private readonly ILogger<ConditionalDeleteResourceHandler> _logger;

        public ConditionalDeleteResourceHandler(
            IFhirDataStore fhirDataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory,
            ISearchService searchService,
            IMediator mediator,
            ResourceIdProvider resourceIdProvider,
            IAuthorizationService<DataActions> authorizationService,
            IDeletionService deleter,
            RequestContextAccessor<IFhirRequestContext> fhirContext,
            IOptions<CoreFeatureConfiguration> configuration,
            ILogger<ConditionalDeleteResourceHandler> logger)
            : base(fhirDataStore, conformanceProvider, resourceWrapperFactory, resourceIdProvider, authorizationService)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(searchService, nameof(searchService));
            EnsureArg.IsNotNull(deleter, nameof(deleter));
            EnsureArg.IsNotNull(configuration.Value, nameof(configuration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _searchService = searchService;
            _deleter = deleter;
            _fhirContext = fhirContext;
            _configuration = configuration.Value;
            _logger = logger;
        }

        public async Task<DeleteResourceResponse> HandleAsync(ConditionalDeleteResourceRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            // Build required permissions: delete permission + read/search permission for conditional operations
            DataActions deletePermissions = request.DeleteOperation == DeleteOperation.SoftDelete ? DataActions.Delete : DataActions.HardDelete | DataActions.Delete;
            DataActions searchPermissions = DataActions.Read | DataActions.Search; // Support both legacy Read and SMART v2 Search
            DataActions requiredPermissions = deletePermissions | searchPermissions; // Include legacy Write support

            var grantedAccess = await AuthorizationService.CheckAccess(requiredPermissions, cancellationToken);

            // Check if user has required delete permissions (granular or legacy Write)
            bool hasDeletePermission = request.DeleteOperation == DeleteOperation.SoftDelete
                ? (grantedAccess & DataActions.Delete) != 0
                : (grantedAccess & (DataActions.HardDelete | DataActions.Delete)) == (DataActions.HardDelete | DataActions.Delete);

            // Check if user has required search permissions for conditional operations
            bool hasSearchPermission = (grantedAccess & (DataActions.Read | DataActions.Search)) != 0;

            if (!hasDeletePermission || !hasSearchPermission)
            {
                throw new UnauthorizedFhirActionException();
            }

            try
            {
                if (request.MaxDeleteCount > 1)
                {
                    return await DeleteMultipleAsync(request, cancellationToken);
                }

                return await DeleteSingleAsync(request, cancellationToken);
            }
            catch (IncompleteOperationException<IReadOnlySet<string>> exception)
            {
                _fhirContext.RequestContext.ResponseHeaders[KnownHeaders.ItemsDeleted] = exception.PartialResults.Count.ToString();
                throw;
            }
        }

        private async Task<DeleteResourceResponse> DeleteSingleAsync(ConditionalDeleteResourceRequest request, CancellationToken cancellationToken)
        {
            var results = await _searchService.ConditionalSearchAsync(
                request.ResourceType,
                request.ConditionalParameters,
                cancellationToken,
                logger: _logger);

            int count = results.Results.Where(result => result.SearchEntryMode == ValueSets.SearchEntryMode.Match).Count();
            bool tooManyIncludeResults = _fhirContext.RequestContext.BundleIssues.Any(
                x => string.Equals(x.Diagnostics, Core.Resources.TruncatedIncludeMessage, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.Diagnostics, Core.Resources.TruncatedIncludeMessageForIncludes, StringComparison.OrdinalIgnoreCase));

            if (count == 0)
            {
                return new DeleteResourceResponse(0);
            }
            else if (count == 1 && !tooManyIncludeResults)
            {
                if (results.Results.Count == 1)
                {
                    var result = await _deleter.DeleteAsync(
                        new DeleteResourceRequest(
                            request.ResourceType,
                            results.Results.First().Resource.ResourceId,
                            request.DeleteOperation,
                            bundleResourceContext: request.BundleResourceContext),
                        cancellationToken);

                    if (string.IsNullOrWhiteSpace(result.VersionId))
                    {
                        return new DeleteResourceResponse(result);
                    }

                    return new DeleteResourceResponse(result, weakETag: WeakETag.FromVersionId(result.VersionId));
                }
                else
                {
                    // Include results were present, use delete multiple to handle them.
                    return await DeleteMultipleAsync(request, cancellationToken);
                }
            }
            else if (count == 1 && tooManyIncludeResults)
            {
                throw new BadRequestException(string.Format(CultureInfo.InvariantCulture, Core.Resources.TooManyIncludeResults, _configuration.DefaultIncludeCountPerSearch, _configuration.MaxIncludeCountPerSearch));
            }
            else
            {
                // Multiple matches: The server returns a 412 Precondition Failed error indicating the client's criteria were not selective enough
                _logger.LogInformation("PreconditionFailed: ConditionalOperationNotSelectiveEnough");
                throw new PreconditionFailedException(string.Format(CultureInfo.InvariantCulture, Core.Resources.ConditionalOperationNotSelectiveEnough, request.ResourceType));
            }
        }

        private async Task<DeleteResourceResponse> DeleteMultipleAsync(ConditionalDeleteResourceRequest request, CancellationToken cancellationToken)
        {
            long numDeleted = (await _deleter.DeleteMultipleAsync(request, cancellationToken)).Sum(result => result.Value);
            return new DeleteResourceResponse((int)numDeleted);
        }
    }
}
