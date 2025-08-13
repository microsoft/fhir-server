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
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Guidance
{
    public class ClinicalReferenceDuplicator : IClinicalReferenceDuplicator
    {
        internal const string TagDuplicateOf = "duplicateOf";
        internal const string TagIsDuplicate = "isDuplicate";

        private readonly IFhirDataStore _dataStore;
        private readonly ISearchService _searchService;
        private readonly IResourceWrapperFactory _resourceWrapperFactory;
        private readonly ResourceIdProvider _resourceIdProvider;
        private readonly ILogger<ClinicalReferenceDuplicator> _logger;

        public ClinicalReferenceDuplicator(
            IFhirDataStore dataStore,
            ISearchService searchService,
            IResourceWrapperFactory resourceWrapperFactory,
            ILogger<ClinicalReferenceDuplicator> logger)
        {
            EnsureArg.IsNotNull(dataStore, nameof(dataStore));
            EnsureArg.IsNotNull(searchService, nameof(searchService));
            EnsureArg.IsNotNull(resourceWrapperFactory, nameof(resourceWrapperFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _dataStore = dataStore;
            _searchService = searchService;
            _resourceWrapperFactory = resourceWrapperFactory;
            _resourceIdProvider = new ResourceIdProvider();
            _logger = logger;
        }

        public async Task<(ResourceWrapper source, ResourceWrapper duplicate)> CreateResourceAsync(
            RawResourceElement rawResourceElement,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(rawResourceElement, nameof(rawResourceElement));
            EnsureArg.IsNotNull(rawResourceElement.RawResource, nameof(rawResourceElement.RawResource));

            try
            {
                var resource = ConvertToResource(rawResourceElement);
                if (!ShouldDuplicate(resource))
                {
                    _logger.LogWarning("A resource doesn't have any attachment with clinical reference.");
                    return (default, default);
                }

                var duplicateResourceWrapper = await CreateDuplicateResourceInternalAsync(
                    resource,
                    cancellationToken);
                var sourceResourceWrapper = await UpdateResourceInternalAsync(
                    resource,
                    duplicateResourceWrapper,
                    cancellationToken);
                return (sourceResourceWrapper, duplicateResourceWrapper);
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
                var duplicateResourceWrappers = await SearchResourceAsync(
                    GetDuplicateResourceType(resourceKey.ResourceType),
                    resourceKey.Id,
                    cancellationToken);
                _logger.LogInformation("Deleting {Count} duplicate resources...", duplicateResourceWrappers.Count);

                var duplicateResourceKeysDeleted = new List<ResourceKey>();
                if (duplicateResourceWrappers.Any())
                {
                    if (duplicateResourceWrappers.Count > 1)
                    {
                        _logger.LogWarning("More than one duplicate resource found.");
                    }

                    foreach (var wrapper in duplicateResourceWrappers)
                    {
                        try
                        {
                            var key = await DeleteDuplicateResourceInternalAsync(
                                wrapper,
                                deleteOperation,
                                cancellationToken);
                            duplicateResourceKeysDeleted.Add(key);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to delete a duplicate resource: {Id}...", wrapper.ResourceId);
                            throw;
                        }
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

        public async Task<IReadOnlyList<ResourceWrapper>> UpdateResourceAsync(
            RawResourceElement rawResourceElement,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(rawResourceElement, nameof(rawResourceElement));
            EnsureArg.IsNotNull(rawResourceElement.RawResource, nameof(rawResourceElement.RawResource));

            try
            {
                var resource = ConvertToResource(rawResourceElement);
                if (ShouldDuplicate(resource))
                {
                    var duplicateResourceWrappers = await SearchResourceAsync(
                        GetDuplicateResourceType(rawResourceElement.InstanceType),
                        rawResourceElement.Id,
                        cancellationToken);
                    _logger.LogInformation("Updating {Count} duplicate resources...", duplicateResourceWrappers.Count);

                    if (duplicateResourceWrappers.Any())
                    {
                        if (duplicateResourceWrappers.Count > 1)
                        {
                            _logger.LogWarning("More than one duplicate resource found.");
                        }

                        var duplicateResourcesUpdated = new List<ResourceWrapper>();
                        foreach (var wrapper in duplicateResourceWrappers)
                        {
                            try
                            {
                                var duplicate = ConvertToResource(wrapper.RawResource);
                                var duplicateUpdated = await UpdateDuplicateResourceInternalAsync(
                                    resource,
                                    duplicate,
                                    cancellationToken);
                                duplicateResourcesUpdated.Add(duplicateUpdated);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to update a duplicate resource: {Id}...", wrapper.ResourceId);
                                throw;
                            }
                        }

                        return duplicateResourcesUpdated;
                    }

                    _logger.LogWarning("No duplicate resources found.");
                    (var sourceWrapper, var duplicateWrapper) = await CreateResourceAsync(
                        rawResourceElement,
                        cancellationToken);
                    return new List<ResourceWrapper> { duplicateWrapper };
                }
                else
                {
                    _logger.LogWarning("A resource doesn't have any attachment with clinical reference.");
                    await DeleteResourceAsync(
                        new ResourceKey(resource.TypeName, resource.Id),
                        DeleteOperation.HardDelete,
                        cancellationToken);
                    return new List<ResourceWrapper>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete a duplicate resource.");
                throw;
            }
        }

        public bool IsDuplicatableResourceType(string resourceType)
        {
            return string.Equals(resourceType, KnownResourceTypes.DiagnosticReport, StringComparison.OrdinalIgnoreCase)
                || string.Equals(resourceType, KnownResourceTypes.DocumentReference, StringComparison.OrdinalIgnoreCase);
        }

        private async Task<ResourceWrapper> CreateDuplicateResourceInternalAsync(
            Resource resource,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            try
            {
                var duplicateResource = CreateDuplicateResource(resource);
                var duplicateResourceWrapper = _resourceWrapperFactory.CreateResourceWrapper(
                    duplicateResource,
                    _resourceIdProvider,
                    false,
                    true);

                _logger.LogInformation(
                    "Creating a duplicate resource '{DuplicateResourceType}' of a '{ResourceType}' resource '{Id}'...",
                    duplicateResource.TypeName,
                    resource.TypeName,
                    resource.Id);
                var outcome = await _dataStore.UpsertAsync(
                    new ResourceWrapperOperation(
                        duplicateResourceWrapper,
                        true,
                        true,
                        null,
                        false,
                        false,
                        null),
                    cancellationToken);

                _logger.LogInformation(
                    "A '{DuplicateResourceType}' resource {Outcome}.",
                    duplicateResource.TypeName,
                    outcome.OutcomeType.ToString().ToLowerInvariant());
                return outcome.Wrapper;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create a duplicate resource.");
                throw;
            }
        }

        private async Task<ResourceKey> DeleteDuplicateResourceInternalAsync(
            ResourceWrapper duplicateResourceWrapper,
            DeleteOperation deleteOperation,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(duplicateResourceWrapper, nameof(duplicateResourceWrapper));

            try
            {
                _logger.LogInformation(
                    "Deleting a duplicate resource '{DuplicateResourceType}': {Id}...",
                    duplicateResourceWrapper.ResourceTypeName,
                    duplicateResourceWrapper.ResourceId);
                if (deleteOperation == DeleteOperation.HardDelete)
                {
                    await _dataStore.HardDeleteAsync(
                        new ResourceKey(duplicateResourceWrapper.ResourceTypeName, duplicateResourceWrapper.ResourceId),
                        false,
                        false,
                        cancellationToken);
                }
                else
                {
                    await _dataStore.UpsertAsync(
                        new ResourceWrapperOperation(
                            duplicateResourceWrapper,
                            false,
                            false,
                            null,
                            false,
                            false,
                            null),
                        cancellationToken);
                }

                return new ResourceKey(duplicateResourceWrapper.ResourceTypeName, duplicateResourceWrapper.ResourceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete a duplicate resource.");
                throw;
            }
        }

        private async Task<ResourceWrapper> UpdateDuplicateResourceInternalAsync(
            Resource resource,
            Resource duplicateResource,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));
            EnsureArg.IsNotNull(duplicateResource, nameof(duplicateResource));

            try
            {
                UpdateDuplicateResource(resource, duplicateResource);

                var duplicateResourceWrapper = _resourceWrapperFactory.Create(
                    duplicateResource.ToResourceElement(),
                    false,
                    true);

                _logger.LogInformation(
                    "Updating a duplicate resource '{DuplicateResourceType}': {Id}...",
                    duplicateResource.TypeName,
                    resource.Id);
                var outcome = await _dataStore.UpsertAsync(
                    new ResourceWrapperOperation(
                        duplicateResourceWrapper,
                        true,
                        true,
                        null,
                        false,
                        false,
                        null),
                    cancellationToken);

                _logger.LogInformation(
                    "A '{DuplicateResourceType}' resource {Outcome}.",
                    duplicateResource.TypeName,
                    outcome.OutcomeType.ToString().ToLowerInvariant());
                return outcome.Wrapper;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update a duplicate resource.");
                throw;
            }
        }

        private async Task<ResourceWrapper> UpdateResourceInternalAsync(
            Resource resource,
            ResourceWrapper duplicateResourceWrapper,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));
            EnsureArg.IsNotNull(duplicateResourceWrapper, nameof(duplicateResourceWrapper));

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

                tags.Add(new Coding(TagDuplicateOf, duplicateResourceWrapper.ResourceId));
                resource.Meta.Tag = tags;

                var resourceWrapper = _resourceWrapperFactory.Create(
                    resource.ToResourceElement(),
                    false,
                    true);

                _logger.LogInformation(
                    "Updating a '{ResourceType}' resource with a duplicate resource '{Id}'...",
                    resource.TypeName,
                    duplicateResourceWrapper.ResourceId);
                var outcome = await _dataStore.UpsertAsync(
                    new ResourceWrapperOperation(
                        resourceWrapper,
                        true,
                        true,
                        null,
                        false,
                        false,
                        null),
                    cancellationToken);

                _logger.LogInformation(
                    "A '{ResourceType}' resource {Outcome}.",
                    resource.TypeName,
                    outcome.OutcomeType.ToString().ToLowerInvariant());
                return outcome.Wrapper;
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

        private static Resource ConvertToResource(RawResource rawResource)
        {
            EnsureArg.IsNotNull(rawResource, nameof(rawResource));

            return rawResource
                .ToITypedElement(ModelInfoProvider.Instance)
                .ToResourceElement()
                .ToPoco();
        }

        private static Resource ConvertToResource(RawResourceElement rawResourceElement)
        {
            EnsureArg.IsNotNull(rawResourceElement, nameof(rawResourceElement));

            return ConvertToResource(rawResourceElement.RawResource);
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

        private static bool ShouldDuplicate(Resource resource)
        {
            if (resource == null)
            {
                return false;
            }

            if (resource.Meta?.Tag?.Any(x => string.Equals(x.System, TagDuplicateOf, StringComparison.OrdinalIgnoreCase)) ?? false)
            {
                return true;
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
