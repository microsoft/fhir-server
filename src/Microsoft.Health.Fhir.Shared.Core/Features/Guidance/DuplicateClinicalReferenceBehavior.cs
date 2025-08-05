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
        internal const string TagDuplicateOf = "duplicateOf";

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
            EnsureArg.IsNotNull(request, nameof(request));
            EnsureArg.IsNotNull(next, nameof(next));

            var response = await next(cancellationToken);
            var resource = response?.Outcome?.RawResourceElement?.RawResource?
                .ToITypedElement(ModelInfoProvider.Instance)?
                .ToResourceElement()?
                .ToPoco();
            if (_coreFeatureConfiguration.EnableClinicalReferenceDuplication && ShouldDuplicate(resource))
            {
                try
                {
                    var resourceTypeToCreate = (resource is DiagnosticReport) ? KnownResourceTypes.DocumentReference : KnownResourceTypes.DiagnosticReport;
                    _logger.LogInformation(
                        "Creating a '{DuplicateResourceType}' resource to duplicate a '{ResourceType}' resource...",
                        resourceTypeToCreate,
                        resource.TypeName);
                    var createResponse = await CreateResourceAsync(
                        resource,
                        cancellationToken);
                    _logger.LogInformation(
                        "A '{DuplicateResourceType}' resource {Outcome}.",
                        resourceTypeToCreate,
                        createResponse?.Outcome?.Outcome.ToString().ToLowerInvariant() ?? "not created");

                    var duplicateResource = createResponse?.Outcome?.RawResourceElement?.RawResource?
                        .ToITypedElement(ModelInfoProvider.Instance)?
                        .ToResourceElement()?
                        .ToPoco();
                    if (duplicateResource != null)
                    {
                        _logger.LogInformation(
                            "Updating a '{ResourceType}' resource with the id of a '{DuplicateResourceType}' resource...",
                            resource.TypeName,
                            resourceTypeToCreate);
                        await UpdateResourceAsync(
                            resource,
                            duplicateResource,
                            cancellationToken);
                    }
                    else
                    {
                        _logger.LogWarning("A response for a create request contains an outcome with a null resource.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to create a duplicate of a '{ResourceType}' resource.",
                        resource.TypeName);
                    throw;
                }
            }

            return response;
        }

        public async Task<UpsertResourceResponse> Handle(
            UpsertResourceRequest request,
            RequestHandlerDelegate<UpsertResourceResponse> next,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));
            EnsureArg.IsNotNull(next, nameof(next));

            var response = await next(cancellationToken);
            var resource = response?.Outcome?.RawResourceElement?.RawResource?
                .ToITypedElement(ModelInfoProvider.Instance)?
                .ToResourceElement()?
                .ToPoco();
            var duplicateResourceId = GetDuplicateResourceId(resource);
            if (_coreFeatureConfiguration.EnableClinicalReferenceDuplication)
            {
                try
                {
                    if (!string.IsNullOrEmpty(duplicateResourceId))
                    {
                        _logger.LogInformation("Searching a duplicate resource for {Id}...", duplicateResourceId);
                        var searchResult = await SearchResourceAsync(
                            (resource is DiagnosticReport) ? KnownResourceTypes.DocumentReference : KnownResourceTypes.DiagnosticReport,
                            duplicateResourceId,
                            cancellationToken);

                        var duplicateResources = searchResult?.Results?.Where(x => x.Resource != null).Select(x => x.Resource).ToList();
                        _logger.LogInformation("Updating {Count} duplicate resources...", duplicateResources.Count);
                        if (duplicateResources.Count > 1)
                        {
                            _logger.LogWarning("More than one duplicate resource found.");
                        }

                        foreach (var duplicate in duplicateResources)
                        {
                            var duplicateResource = duplicate.RawResource?
                                .ToITypedElement(ModelInfoProvider.Instance)?
                                .ToResourceElement()?
                                .ToPoco();
                            if (duplicateResource == null)
                            {
                                _logger.LogWarning("Failed to convert a resource element to a poco: {Id}", duplicate.ResourceId);
                                continue;
                            }

                            UpdateDuplicateResource(
                                resource,
                                duplicateResource);
                            await _mediator.Send<UpsertResourceResponse>(
                                new UpsertResourceRequest(duplicateResource.ToResourceElement()),
                                cancellationToken);
                        }

                        return response;
                    }

                    var resourceTypeToCreate = (resource is DiagnosticReport) ? KnownResourceTypes.DocumentReference : KnownResourceTypes.DiagnosticReport;
                    _logger.LogInformation(
                        "Creating a '{DuplicateResourceType}' resource to duplicate a '{ResourceType}' resource...",
                        resourceTypeToCreate,
                        resource.TypeName);
                    await CreateResourceAsync(
                        resource,
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to update a duplicate of a '{ResourceType}' resource.",
                        resource.TypeName);
                    throw;
                }
            }

            return response;
        }

        public async Task<DeleteResourceResponse> Handle(
            DeleteResourceRequest request,
            RequestHandlerDelegate<DeleteResourceResponse> next,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));
            EnsureArg.IsNotNull(next, nameof(next));

            var response = await next(cancellationToken);
            var resourceKey = request?.ResourceKey;
            if (_coreFeatureConfiguration.EnableClinicalReferenceDuplication
                && (string.Equals(resourceKey?.ResourceType, KnownResourceTypes.DiagnosticReport, StringComparison.OrdinalIgnoreCase)
                || string.Equals(resourceKey?.ResourceType, KnownResourceTypes.DocumentReference, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    _logger.LogInformation("Searching a duplicate resource for {Id}...", resourceKey.Id);
                    var searchResult = await SearchResourceAsync(
                        string.Equals(resourceKey.ResourceType, KnownResourceTypes.DiagnosticReport, StringComparison.OrdinalIgnoreCase)
                            ? KnownResourceTypes.DocumentReference : KnownResourceTypes.DiagnosticReport,
                        resourceKey.Id,
                        cancellationToken);

                    var duplicateResources = searchResult.Results.Where(x => x.Resource != null).Select(x => x.Resource).ToList();
                    _logger.LogInformation("Deleting {Count} duplicate resources...", duplicateResources.Count);
                    if (duplicateResources.Count > 1)
                    {
                        _logger.LogWarning("More than one duplicate resource found.");
                    }

                    foreach (var duplicate in duplicateResources)
                    {
                        _logger.LogInformation("Deleting a duplicate resource: {Id}...", duplicate.ResourceId);
                        await _mediator.Send(
                            new DeleteResourceRequest(
                                new ResourceKey(duplicate.ResourceTypeName, duplicate.ResourceId),
                                request.DeleteOperation),
                            cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to delete a duplicate of a '{ResourceType}' resource.",
                        resourceKey.ResourceType);
                    throw;
                }
            }

            return response;
        }

        private Task<UpsertResourceResponse> CreateResourceAsync(
            Resource resource,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var duplicateResource = CreateDuplicateResource(resource);
            var request = new CreateResourceRequest(duplicateResource.ToResourceElement());

            return _mediator.Send<UpsertResourceResponse>(
                request,
                cancellationToken);
        }

        private Task<UpsertResourceResponse> UpdateResourceAsync(
            Resource resource,
            Resource duplicateResource,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));
            EnsureArg.IsNotNull(duplicateResource, nameof(duplicateResource));

            if (resource.Meta == null)
            {
                resource.Meta = new Meta();
            }

            if (resource.Meta.Tag == null)
            {
                resource.Meta.Tag = new List<Coding>();
            }

            resource.Meta.Tag.Add(new Coding(TagDuplicateOf, duplicateResource.Id));
            return _mediator.Send<UpsertResourceResponse>(
                new UpsertResourceRequest(resource.ToResourceElement()),
                cancellationToken);
        }

        private Task<SearchResult> SearchResourceAsync(
            string resourceType,
            string resourceId,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resourceType, nameof(resourceType));
            EnsureArg.IsNotNull(resourceId, nameof(resourceId));

            var queryParameters = new List<Tuple<string, string>>
            {
                Tuple.Create("_tag", $"{TagDuplicateOf}|{resourceId}"),
            };

            return _searchService.SearchAsync(
                resourceType,
                queryParameters,
                cancellationToken);
        }

        private static Resource CreateDuplicateResource(Resource resource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            if (!(resource is DiagnosticReport || resource is DocumentReference))
            {
                throw new ArgumentException(
                    $"A resource to be duplicated must be of type '{nameof(DiagnosticReport)}' or '{nameof(DocumentReference)}'.");
            }

            if (resource is DiagnosticReport)
            {
                var diagnosticReportToDuplicate = (DiagnosticReport)resource;

                // TODO: more fields need to be populated?
                var documentReference = new DocumentReference
                {
                    Meta = new Meta
                    {
                        Tag = new List<Coding>
                        {
                            new Coding(TagDuplicateOf, diagnosticReportToDuplicate.Id),
                        },
                    },
                    Content = new List<DocumentReference.ContentComponent>(),
                    Subject = diagnosticReportToDuplicate.Subject,
#if R4 || R4B || Stu3
                    Status = DocumentReferenceStatus.Current,
#else
                    Status = DocumentReference.DocumentReferenceStatus.Current,
#endif
                };

                if (diagnosticReportToDuplicate.PresentedForm?.Any(x => x.Url != null) ?? false)
                {
                    // TODO: need to be more selective about whether the resource should be duplicated based on urls.
                    // (https://hl7.org/fhir/us/core/STU6.1/clinical-notes.html#clinical-notes-1)
                    foreach (var attachment in diagnosticReportToDuplicate.PresentedForm.Where(x => x.Url != null))
                    {
                        documentReference.Content.Add(
                            new DocumentReference.ContentComponent
                            {
                                Attachment = attachment,
                            });
                    }
                }

                return documentReference;
            }

            var documentReferenceToDuplicate = (DocumentReference)resource;

            // TODO: more fields need to be populated?
            var diagnosticReport = new DiagnosticReport
            {
                Meta = new Meta
                {
                    Tag = new List<Coding>
                    {
                        new Coding(TagDuplicateOf, resource.Id),
                    },
                },
                PresentedForm = new List<Attachment>(),
                Subject = documentReferenceToDuplicate.Subject,
                Status = DiagnosticReport.DiagnosticReportStatus.Registered,
            };

            if (documentReferenceToDuplicate.Content?.Any(x => x.Attachment?.Url != null) ?? false)
            {
                // TODO: need to be more selective about whether the resource should be duplicated based on urls.
                // (https://hl7.org/fhir/us/core/STU6.1/clinical-notes.html#clinical-notes-1)
                foreach (var attachment in documentReferenceToDuplicate.Content?.Where(x => x.Attachment?.Url != null).Select(x => x.Attachment))
                {
                    diagnosticReport.PresentedForm.Add(attachment);
                }
            }

            return diagnosticReport;
        }

        private static string GetDuplicateResourceId(Resource resource)
        {
            return resource?.Meta?.Tag?.SingleOrDefault(x => x.System == TagDuplicateOf)?.Code;
        }

        private static bool ShouldDuplicate(Resource resource)
        {
            if (resource == null)
            {
                return false;
            }

            if (resource is DiagnosticReport)
            {
                // TODO: need to be more selective about whether the resource should be duplicated based on urls.
                // (https://hl7.org/fhir/us/core/STU6.1/clinical-notes.html#clinical-notes-1)
                return ((DiagnosticReport)resource).PresentedForm?.Any(x => x.Url != null) ?? false;
            }
            else if (resource is DocumentReference)
            {
                // TODO: need to be more selective about whether the resource should be duplicated based on urls.
                // (https://hl7.org/fhir/us/core/STU6.1/clinical-notes.html#clinical-notes-1)
                return ((DocumentReference)resource).Content?.Any(x => x.Attachment?.Url != null) ?? false;
            }

            return false;
        }

        private static void UpdateDuplicateResource(
            Resource resource,
            Resource duplicateResource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));
            EnsureArg.IsNotNull(duplicateResource, nameof(duplicateResource));

            if (!(resource is DiagnosticReport || resource is DocumentReference)
                || !(duplicateResource is DiagnosticReport || duplicateResource is DocumentReference)
                || string.Equals(resource?.TypeName, duplicateResource?.TypeName, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"A source/target resource to be updated must be of type '{nameof(DiagnosticReport)}' and '{nameof(DocumentReference)}'.");
            }

            if (resource is DiagnosticReport)
            {
                var diagnosticReport = (DiagnosticReport)resource;
                var documentReference = (DocumentReference)duplicateResource;

                documentReference.Subject = diagnosticReport.Subject;
                if (documentReference.Content == null)
                {
                    documentReference.Content = new List<DocumentReference.ContentComponent>();
                }

                // TODO: is clearing the entire content necessary and right?
                documentReference.Content.Clear();
                if (diagnosticReport.PresentedForm?.Any(x => x.Url != null) ?? false)
                {
                    foreach (var attachment in diagnosticReport.PresentedForm.Where(x => x.Url != null))
                    {
                        documentReference.Content.Add(
                            new DocumentReference.ContentComponent
                            {
                                Attachment = attachment,
                            });
                    }
                }
            }
            else
            {
                var documentReference = (DocumentReference)resource;
                var diagnosticReport = (DiagnosticReport)duplicateResource;

                diagnosticReport.Subject = documentReference.Subject;
                if (diagnosticReport.PresentedForm == null)
                {
                    diagnosticReport.PresentedForm = new List<Attachment>();
                }

                // TODO: is clearing the entire presented-form necessary and right?
                diagnosticReport.PresentedForm.Clear();
                if (documentReference.Content?.Any(x => x.Attachment?.Url != null) ?? false)
                {
                    foreach (var attachment in documentReference.Content?.Where(x => x.Attachment?.Url != null).Select(x => x.Attachment))
                    {
                        diagnosticReport.PresentedForm.Add(attachment);
                    }
                }
            }
        }
    }
}
