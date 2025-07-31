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
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Shared.Core.Features.Guidance
{
    public class DuplicateClinicalReferenceBehavior : IPipelineBehavior<CreateResourceRequest, UpsertResourceResponse>,
        IPipelineBehavior<UpsertResourceRequest, UpsertResourceResponse>,
        IPipelineBehavior<DeleteResourceRequest, DeleteResourceResponse>
    {
        private readonly IMediator _mediator;
        private readonly ISearchService _searchService;
        private readonly CoreFeatureConfiguration _coreFeatureConfiguration;
        private readonly ILogger<DuplicateClinicalReferenceBehavior> _logger;

        public DuplicateClinicalReferenceBehavior(
            IMediator mediator,
            ISearchService searchService,
            IOptions<CoreFeatureConfiguration> coreFeatureConfiguration,
            ILogger<DuplicateClinicalReferenceBehavior> logger)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(searchService, nameof(searchService));
            EnsureArg.IsNotNull(coreFeatureConfiguration?.Value, nameof(coreFeatureConfiguration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _mediator = mediator;
            _searchService = searchService;
            _coreFeatureConfiguration = coreFeatureConfiguration.Value;
            _logger = logger;
        }

        public async Task<UpsertResourceResponse> Handle(
            CreateResourceRequest request,
            RequestHandlerDelegate<UpsertResourceResponse> next,
            CancellationToken cancellationToken)
        {
            var response = await next(cancellationToken);
            var resource = response?.Outcome?.RawResourceElement?.RawResource?.ToITypedElement(ModelInfoProvider.Instance)?.ToResourceElement();
            if (_coreFeatureConfiguration.EnableClinicalReferenceDuplication
                && resource != null
                && (string.Equals(resource.InstanceType, KnownResourceTypes.DiagnosticReport, StringComparison.OrdinalIgnoreCase)
                || string.Equals(resource.InstanceType, KnownResourceTypes.DocumentReference, StringComparison.OrdinalIgnoreCase)))
            {
                // TODO: need to differentiate urls since not all urls are of clinical notes (https://hl7.org/fhir/us/core/STU6.1/clinical-notes.html#fhir-resources-to-exchange-clinical-notes)
                var url = GetUrl(resource);
                if (!string.IsNullOrWhiteSpace(url))
                {
                    _logger.LogInformation($"A url found in '{resource.InstanceType}' resource: {url}");

                    var searchResult = await SearchDeplicateResourceAsync(
                        resource,
                        url,
                        cancellationToken);
                    var found = searchResult?.Results?.Any() ?? false;
                    if (!found)
                    {
                        _logger.LogInformation($"Creating a duplicate resource of '{resource.InstanceType}' resource...");
                        var duplicateResource = CreateDuplicateResource(resource, url);
                        var createRequest = new CreateResourceRequest(duplicateResource);
                        var createResponse = await _mediator.Send<UpsertResourceResponse>(
                            createRequest,
                            cancellationToken);
                        if (createResponse?.Outcome?.Outcome == SaveOutcomeType.Created)
                        {
                            _logger.LogInformation($"A duplicate resource of '{resource.InstanceType}' resource created.");
                        }
                    }
                    else
                    {
                        _logger.LogInformation($"A duplicate resource of '{resource.InstanceType}' resource already exists.");
                    }
                }
            }

            return response;
        }

        public Task<UpsertResourceResponse> Handle(
            UpsertResourceRequest request,
            RequestHandlerDelegate<UpsertResourceResponse> next,
            CancellationToken cancellationToken)
        {
            return next(cancellationToken);
        }

        public Task<DeleteResourceResponse> Handle(
            DeleteResourceRequest request,
            RequestHandlerDelegate<DeleteResourceResponse> next,
            CancellationToken cancellationToken)
        {
            return next(cancellationToken);
        }

        private Task<SearchResult> SearchDeplicateResourceAsync(
            ResourceElement resourceElement,
            string url,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resourceElement, nameof(resourceElement));
            EnsureArg.IsNotNull(url, nameof(url));

            var isDiagnosticReport = string.Equals(resourceElement.InstanceType, KnownResourceTypes.DiagnosticReport, StringComparison.OrdinalIgnoreCase);
            var queryParameters = new List<Tuple<string, string>>
            {
                Tuple.Create("_tag", url),
            };

            return _searchService.SearchAsync(
                isDiagnosticReport ? KnownResourceTypes.DocumentReference : KnownResourceTypes.DiagnosticReport,
                queryParameters,
                cancellationToken);
        }

        private static ResourceElement CreateDuplicateResource(
            ResourceElement resourceElement,
            string url)
        {
            EnsureArg.IsNotNull(resourceElement, nameof(resourceElement));
            EnsureArg.IsNotNull(url, nameof(url));

            var isDiagnosticReport = string.Equals(resourceElement.InstanceType, KnownResourceTypes.DiagnosticReport, StringComparison.OrdinalIgnoreCase);
            if (isDiagnosticReport)
            {
                // TODO: more fields need to be populated?
                var documentReference = new DocumentReference
                {
                    Meta = new Meta
                    {
                        Tag = new List<Coding>
                        {
                            new Coding("url", url),
                        },
                    },
#if R4 || R4B || Stu3
                    Status = DocumentReferenceStatus.Current,
#else
                    Status = DocumentReference.DocumentReferenceStatus.Current,
#endif
                };

                return documentReference.ToResourceElement();
            }

            // TODO: more fields need to be populated?
            var diagnosticReport = new DiagnosticReport
            {
                Meta = new Meta
                {
                    Tag = new List<Coding>
                        {
                            new Coding("url", url),
                        },
                },
                Status = DiagnosticReport.DiagnosticReportStatus.Registered,
            };

            return diagnosticReport.ToResourceElement();
        }

        private static string GetUrl(ResourceElement resourceElement)
        {
            if (resourceElement == null)
            {
                return null;
            }

            var path = string.Equals(resourceElement.InstanceType, KnownResourceTypes.DiagnosticReport, StringComparison.OrdinalIgnoreCase)
                ? "DiagnosticReport.presentedForm.url"
                : "DocumentReference.content.attachment.url";
            return resourceElement.Scalar<string>(path);
        }
    }
}
