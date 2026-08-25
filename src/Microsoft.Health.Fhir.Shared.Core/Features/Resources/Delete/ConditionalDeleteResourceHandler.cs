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
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
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

            await AuthorizationService.CheckConditionalDeleteAccess(
                cancellationToken,
                request.DeleteOperation != DeleteOperation.SoftDelete);

            // A single If-Match version describes exactly one resource. Once the request allows more than one
            // Match to be deleted there is no correct way to honor it: applying the tag to one arbitrary Match,
            // applying it to every Match, or dropping it would each either guard the wrong resource or silently
            // delete unguarded. Reject the combination before searching or deleting anything.
            if (request.WeakETag != null && request.MaxDeleteCount > 1)
            {
                _logger.LogInformation("BadRequest: ConditionalDeleteMultipleWithIfMatch");
                throw new BadRequestException(string.Format(
                    CultureInfo.InvariantCulture,
                    Core.Resources.ConditionalDeleteMultipleWithIfMatch,
                    KnownQueryParameterNames.Count));
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
                ResourceWrapper resourceWrapper = results.Results.Single(result => result.SearchEntryMode == ValueSets.SearchEntryMode.Match).Resource;

                if (request.WeakETag != null && request.WeakETag.VersionId != resourceWrapper.Version)
                {
                    _logger.LogInformation("PreconditionFailed: ResourceVersionConflict");
                    throw new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, request.WeakETag.VersionId));
                }

                if (results.Results.Count == 1)
                {
                    // The single-Match, no-Include path guards the delete with the version the conditional
                    // search itself observed. If the data store projection did not surface one, the
                    // precondition cannot be evaluated at all, and silently continuing would downgrade a
                    // guarded delete into an unguarded one. Fail closed before invoking the deletion service,
                    // mirroring the Match-with-Includes guard in DeletionService.GetGuardedMatchVersion, and
                    // avoid formatting an absent version into an empty expected version.
                    if (string.IsNullOrEmpty(resourceWrapper.Version))
                    {
                        _logger.LogInformation("PreconditionFailed: ConditionalDeleteMatchVersionUnavailable");
                        throw new PreconditionFailedException(string.Format(
                            Core.Resources.ConditionalDeleteMatchVersionUnavailable,
                            resourceWrapper.ResourceTypeName,
                            resourceWrapper.ResourceId));
                    }

                    var result = await _deleter.DeleteAsync(
                        new DeleteResourceRequest(
                            request.ResourceType,
                            resourceWrapper.ResourceId,
                            request.DeleteOperation,
                            bundleResourceContext: request.BundleResourceContext,
                            weakETag: request.WeakETag,
                            comparedVersion: resourceWrapper.Version),
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
                    ConditionalDeleteResourceRequest deleteRequest = request.Clone();
                    deleteRequest.IsSingleResourceConditionalDelete = true;
                    return await DeleteMultipleAsync(deleteRequest, cancellationToken);
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
