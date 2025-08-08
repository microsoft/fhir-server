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
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Guidance
{
    public class ClinicalReferenceDuplicator : IClinicalReferenceDuplicator
    {
        internal const string TagDuplicateOf = "duplicateOf";
        internal const string TagIsDuplicate = "isDuplicate";

        private readonly IMediator _mediator;
        private readonly ISearchService _searchService;
        private readonly ILogger<ClinicalReferenceDuplicator> _logger;

        public ClinicalReferenceDuplicator(
            IMediator mediator,
            ISearchService searchService,
            ILogger<ClinicalReferenceDuplicator> logger)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(searchService, nameof(searchService));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _mediator = mediator;
            _searchService = searchService;
            _logger = logger;
        }

        public async Task<Resource> CreateResourceAsync(
            Resource resource,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            try
            {
                var createResponse = await CreateDuplicateResourceInternalAsync(
                    resource,
                    cancellationToken);
                var duplicateResource = createResponse?.Outcome?.RawResourceElement?.RawResource?
                    .ToITypedElement(ModelInfoProvider.Instance)?
                    .ToResourceElement()?
                    .ToPoco();
                if (duplicateResource == null)
                {
                    // TODO: throw an exception here.
                    _logger.LogError("A create response contains a null resource.");
                }

                _logger.LogInformation(
                    "A '{DuplicateResourceType}' resource {Outcome}.",
                    duplicateResource.TypeName,
                    createResponse?.Outcome?.Outcome.ToString().ToLowerInvariant() ?? "not created");

                var updateResponse = await UpdateResourceInternalAsync(
                    resource,
                    duplicateResource,
                    cancellationToken);
                _logger.LogInformation(
                    "A '{ResourceType}' resource {Outcome}.",
                    resource.TypeName,
                    updateResponse?.Outcome?.Outcome.ToString().ToLowerInvariant() ?? "not updated.");
                return duplicateResource;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create a duplicate resource and update a resource with its id.");
                throw;
            }
        }

        public async Task<IReadOnlyList<ResourceKey>> DeleteResourceAsync(
            ResourceKey resourceKey,
            DeleteOperation deleteOperation,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resourceKey, nameof(resourceKey));

            try
            {
                var duplicateResources = await SearchResourceAsync(
                    GetDuplicateResourceType(resourceKey.ResourceType),
                    resourceKey.Id,
                    cancellationToken);
                _logger.LogInformation("Deleting {Count} duplicate resources...", duplicateResources.Count);
                if (duplicateResources.Count > 1)
                {
                    _logger.LogWarning("More than one duplicate resource found.");
                }

                var duplicateResourceKeysDeleted = new List<ResourceKey>();
                foreach (var duplicate in duplicateResources)
                {
                    try
                    {
                        _logger.LogInformation("Deleting a duplicate resource: {Id}...", duplicate.ResourceId);
                        var response = await _mediator.Send(
                            new DeleteResourceRequest(
                                new ResourceKey(duplicate.ResourceTypeName, duplicate.ResourceId),
                                deleteOperation),
                            cancellationToken);
                        duplicateResourceKeysDeleted.Add(response.ResourceKey);
                    }
                    catch (Exception ex)
                    {
                        // Ignore an exception and continue deleting the rest.
                        _logger.LogError(ex, "Failed to delete a duplicate resource: {Id}...", duplicate.ResourceId);
                    }
                }

                return duplicateResourceKeysDeleted;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete a duplicate resource.");
                throw;
            }
        }

        public async Task<IReadOnlyList<ResourceWrapper>> SearchResourceAsync(
            string duplicateResourceType,
            string resourceId,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(duplicateResourceType, nameof(duplicateResourceType));
            EnsureArg.IsNotNull(resourceId, nameof(resourceId));

            try
            {
                var queryParameters = new List<Tuple<string, string>>
                {
                    Tuple.Create("_tag", $"{TagDuplicateOf}|{resourceId}"),
                };

                _logger.LogInformation(
                    "Searching a duplicate resource '{DuplicateResourceType}' of a resource '{Id}'...",
                    duplicateResourceType,
                    resourceId);

                var resources = new List<ResourceWrapper>();
                string continuationToken = null;
                do
                {
                    var searchResult = await _searchService.SearchAsync(
                        duplicateResourceType,
                        queryParameters,
                        cancellationToken);
                    if (searchResult.Results.Any())
                    {
                        resources.AddRange(
                            searchResult.Results
                                .Where(x => x.Resource?.RawResource != null)
                                .Select(x => x.Resource));
                    }

                    continuationToken = searchResult.ContinuationToken;
                }
                while (!string.IsNullOrEmpty(continuationToken));

                return resources;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to search a duplicate resource.");
                throw;
            }
        }

        public async Task<IReadOnlyList<Resource>> UpdateResourceAsync(
            Resource resource,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            try
            {
                var duplicateResourceWrappers = await SearchResourceAsync(
                    GetDuplicateResourceType(resource.TypeName),
                    resource.Id,
                    cancellationToken);
                var duplicateResources = duplicateResourceWrappers
                    .Where(x => x.RawResource != null)
                    .Select(x => x.RawResource.ToITypedElement(ModelInfoProvider.Instance).ToResourceElement().ToPoco()).ToList();
                _logger.LogInformation("Updating {Count} duplicate resources...", duplicateResources.Count);
                if (duplicateResources.Any())
                {
                    if (duplicateResources.Count > 1)
                    {
                        _logger.LogWarning("More than one duplicate resource found.");
                    }

                    var duplicateResourcesUpdated = new List<Resource>();
                    foreach (var duplicate in duplicateResources)
                    {
                        try
                        {
                            _logger.LogInformation("Updating a duplicate resource: {Id}...", duplicate.Id);
                            var response = await UpdateDuplicateResourceInternalAsync(
                                resource,
                                duplicate,
                                cancellationToken);
                            var duplicateUpdated = response.Outcome.RawResourceElement?.RawResource?
                                .ToITypedElement(ModelInfoProvider.Instance)?
                                .ToResourceElement()?
                                .ToPoco();
                            if (duplicateUpdated == null)
                            {
                                _logger.LogError("A update response contains a null resource.");
                                continue;
                            }

                            duplicateResourcesUpdated.Add(duplicateUpdated);
                        }
                        catch (Exception ex)
                        {
                            // Ignore an exception and continue updating the rest.
                            _logger.LogError(ex, "Failed to update a duplicate resource: {Id}...", duplicate.Id);
                        }
                    }

                    return duplicateResourcesUpdated;
                }

                _logger.LogWarning("No duplicate resource found.");
                var duplicateResourceCreated = await CreateResourceAsync(
                    resource,
                    cancellationToken);

                return new List<Resource> { duplicateResourceCreated };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete a duplicate resource.");
                throw;
            }
        }

        public bool CheckDuplicate(ResourceKey resourceKey)
        {
            return string.Equals(resourceKey?.ResourceType, KnownResourceTypes.DiagnosticReport, StringComparison.OrdinalIgnoreCase)
                || string.Equals(resourceKey?.ResourceType, KnownResourceTypes.DocumentReference, StringComparison.OrdinalIgnoreCase);
        }

        public bool ShouldDuplicate(Resource resource)
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

        private Task<UpsertResourceResponse> CreateDuplicateResourceInternalAsync(
            Resource resource,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            try
            {
                var duplicateResource = CreateDuplicateResource(resource);
                var request = new CreateResourceRequest(duplicateResource.ToResourceElement());

                _logger.LogInformation(
                    "Creating a duplicate resource '{DuplicateResourceType}' of a '{ResourceType}' resource '{Id}'...",
                    duplicateResource.TypeName,
                    resource.TypeName,
                    resource.Id);
                return _mediator.Send<UpsertResourceResponse>(
                    request,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create a duplicate resource.");
                throw;
            }
        }

        private Task<UpsertResourceResponse> UpdateResourceInternalAsync(
            Resource resource,
            Resource duplicateResource,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));
            EnsureArg.IsNotNull(duplicateResource, nameof(duplicateResource));

            try
            {
                if (resource.Meta == null)
                {
                    resource.Meta = new Meta();
                }

                var tags = new List<Coding>();
                if (resource.Meta.Tag != null)
                {
                    tags.AddRange(
                        resource.Meta.Tag.Where(x => !string.Equals(x.System, TagDuplicateOf, StringComparison.OrdinalIgnoreCase)));
                }

                tags.Add(new Coding(TagDuplicateOf, duplicateResource.Id));
                resource.Meta.Tag = tags;

                _logger.LogInformation(
                    "Updating a '{ResourceType}' resource with a duplicate resource '{Id}'...",
                    resource.TypeName,
                    duplicateResource.Id);
                return _mediator.Send<UpsertResourceResponse>(
                    new UpsertResourceRequest(resource.ToResourceElement()),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update a resource with a duplicate resource id.");
                throw;
            }
        }

        private Task<UpsertResourceResponse> UpdateDuplicateResourceInternalAsync(
            Resource resource,
            Resource duplicateResource,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));
            EnsureArg.IsNotNull(duplicateResource, nameof(duplicateResource));

            try
            {
                UpdateDuplicateResource(resource, duplicateResource);
                _logger.LogInformation(
                    "Updating a '{DuplicateResourceType}' resource with a duplicate resource '{Id}'...",
                    resource.TypeName,
                    duplicateResource.Id);
                return _mediator.Send<UpsertResourceResponse>(
                    new UpsertResourceRequest(duplicateResource.ToResourceElement()),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update a resource with a duplicate resource id.");
                throw;
            }
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
                            new Coding(TagIsDuplicate, bool.TrueString.ToLowerInvariant()),
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
                        new Coding(TagIsDuplicate, bool.TrueString.ToLowerInvariant()),
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
            EnsureArg.IsNotNull(resource, nameof(resource));

            return resource?.Meta?.Tag?.SingleOrDefault(x => x.System == TagDuplicateOf)?.Code;
        }

        private static string GetDuplicateResourceType(string resourceType)
        {
            EnsureArg.IsNotNullOrEmpty(resourceType, nameof(resourceType));

            return string.Equals(resourceType, KnownResourceTypes.DiagnosticReport, StringComparison.OrdinalIgnoreCase)
                ? KnownResourceTypes.DocumentReference : KnownResourceTypes.DiagnosticReport;
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

                var contents = new List<DocumentReference.ContentComponent>();
                if (documentReference.Content != null)
                {
                    // TODO: save attachments without a url. is this right?
                    contents.AddRange(documentReference.Content.Where(x => string.IsNullOrEmpty(x.Attachment?.Url)));
                }

                if (diagnosticReport.PresentedForm?.Any(x => !string.IsNullOrEmpty(x.Url)) ?? false)
                {
                    foreach (var attachment in diagnosticReport.PresentedForm.Where(x => x.Url != null))
                    {
                        contents.Add(
                            new DocumentReference.ContentComponent
                            {
                                Attachment = attachment,
                            });
                    }
                }

                documentReference.Content = contents;
            }
            else
            {
                var documentReference = (DocumentReference)resource;
                var diagnosticReport = (DiagnosticReport)duplicateResource;
                diagnosticReport.Subject = documentReference.Subject;

                var attachments = new List<Attachment>();
                if (diagnosticReport.PresentedForm != null)
                {
                    // TODO: save attachments without a url. is this right?
                    attachments.AddRange(diagnosticReport.PresentedForm.Where(x => string.IsNullOrEmpty(x.Url)));
                }

                if (documentReference.Content?.Any(x => !string.IsNullOrEmpty(x.Attachment?.Url)) ?? false)
                {
                    foreach (var attachment in documentReference.Content?.Where(x => x.Attachment?.Url != null).Select(x => x.Attachment))
                    {
                        attachments.Add(attachment);
                    }
                }

                diagnosticReport.PresentedForm = attachments;
            }
        }
    }
}
