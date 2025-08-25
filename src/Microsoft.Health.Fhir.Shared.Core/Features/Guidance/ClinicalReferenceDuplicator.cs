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
        internal const string TagDuplicateCreatedOn = "duplicateCreatedOn";
        internal const string TagDuplicateOf = "duplicateOf";

        // See the link for more info, https://hl7.org/fhir/us/core/STU6.1/clinical-notes.html#clinical-notes
        internal static readonly HashSet<string> ClinicalReferenceSystems = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase)
        {
            "https://loinc.org",
        };

        // See the link for more info, https://hl7.org/fhir/us/core/STU6.1/clinical-notes.html#clinical-notes
        internal static readonly HashSet<string> ClinicalReferenceCodes = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase)
        {
            "11488-4",
            "18842-5",
            "34117-2",
            "28570-0",
            "11506-3",
            "18748-4",
            "11502-2",
            "11526-1",
        };

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

        public Task<IReadOnlyList<ResourceWrapper>> CreateResourceAsync(
            RawResourceElement rawResourceElement,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(rawResourceElement, nameof(rawResourceElement));
            EnsureArg.IsNotNull(rawResourceElement.RawResource, nameof(rawResourceElement.RawResource));

            return UpsertDuplicateResourceInternalAsync(
                rawResourceElement,
                cancellationToken);
        }

        public async Task<IReadOnlyList<ResourceKey>> DeleteResourceAsync(
            ResourceKey resourceKey,
            DeleteOperation deleteOperation,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resourceKey, nameof(resourceKey));

            try
            {
                // Steps:
                // 1. Search a duplicate resource(s), tagged with the id of a deleted (source) resource.
                //    1.1. If the search result has a resource(s), delete it.
                //    1.2. If the search result has no resources, no actions.
                // 3. Return the duplicate resource(s) deleted.
                var duplicateResourceWrappers = await SearchDuplicateResourceAsync(
                    resourceKey,
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

        public Task<IReadOnlyList<ResourceWrapper>> UpdateResourceAsync(
            RawResourceElement rawResourceElement,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(rawResourceElement, nameof(rawResourceElement));
            EnsureArg.IsNotNull(rawResourceElement.RawResource, nameof(rawResourceElement.RawResource));

            return UpsertDuplicateResourceInternalAsync(
                rawResourceElement,
                cancellationToken);
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

        private async Task<IReadOnlyList<ResourceWrapper>> SearchDuplicateResourceAsync(
            Resource resource,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            try
            {
                var duplicateResourceType = GetDuplicateResourceType(resource.TypeName);
                var queryParameters = new List<Tuple<string, string>>();
                if (string.Equals(resource.TypeName, KnownResourceTypes.DiagnosticReport, StringComparison.OrdinalIgnoreCase))
                {
                    var diagnosticReport = (DiagnosticReport)resource;
                    var codes = GetClinicalReferenceCodes(diagnosticReport)
                        .Select(x => ClinicalReferenceDuplicatorHelper.ConvertToString(x))
                        .ToList();
#if R4 || R4B || Stu3
                    queryParameters.Add(Tuple.Create("format", string.Join(",", codes)));
#else
                    queryParameters.Add(Tuple.Create("format-code", string.Join(",", codes)));
#endif
                    if (!string.IsNullOrEmpty(diagnosticReport.Subject?.Reference))
                    {
                        queryParameters.Add(Tuple.Create("subject", diagnosticReport.Subject.Reference));
                    }
                }
                else
                {
                    var documentReference = (DocumentReference)resource;
                    var codes = GetClinicalReferenceCodes(documentReference)
                        .Select(x => ClinicalReferenceDuplicatorHelper.ConvertToString(x))
                        .ToList();
                    queryParameters.Add(Tuple.Create("code", string.Join(",", codes)));
                    if (!string.IsNullOrEmpty(documentReference.Subject?.Reference))
                    {
                        queryParameters.Add(Tuple.Create("subject", documentReference.Subject.Reference));
                    }
                }

                _logger.LogInformation(
                    "Searching a duplicate resource '{DuplicateResourceType}' of a resource '{Id}'...",
                    duplicateResourceType,
                    resource.Id);

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

        private async Task<IReadOnlyList<ResourceWrapper>> SearchDuplicateResourceAsync(
            ResourceKey resourceKey,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resourceKey, nameof(resourceKey));

            try
            {
                var duplicateResourceType = GetDuplicateResourceType(resourceKey.ResourceType);
                var queryParameters = new List<Tuple<string, string>>
                {
                    Tuple.Create("_tag", $"{TagDuplicateOf}|{resourceKey.Id}"),
                };

                _logger.LogInformation(
                    "Searching a duplicate resource '{DuplicateResourceType}' of a resource '{Id}'...",
                    duplicateResourceType,
                    resourceKey.Id);

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

        private async Task<ResourceWrapper> UpdateDuplicateResourceInternalAsync(
            Resource resource,
            ResourceWrapper duplicateResourceWrapper,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));
            EnsureArg.IsNotNull(duplicateResourceWrapper, nameof(duplicateResourceWrapper));

            try
            {
                var duplicateResource = ConvertToResource(duplicateResourceWrapper.RawResource);
                var duplicateResourceUpdated = UpdateDuplicateResource(
                    resource,
                    duplicateResource);
                if (!duplicateResourceUpdated)
                {
                    _logger.LogInformation(
                        "Updating a duplicate resource '{DuplicateResourceType}' unnecessary: {Id}...",
                        duplicateResource.TypeName,
                        resource.Id);
                    return duplicateResourceWrapper;
                }

                var duplicateResourceWrapperToUpdate = _resourceWrapperFactory.Create(
                    duplicateResource.ToResourceElement(),
                    false,
                    true);

                _logger.LogInformation(
                    "Updating a duplicate resource '{DuplicateResourceType}': {Id}...",
                    duplicateResource.TypeName,
                    resource.Id);
                var outcome = await _dataStore.UpsertAsync(
                    new ResourceWrapperOperation(
                        duplicateResourceWrapperToUpdate,
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

        private async Task<IReadOnlyList<ResourceWrapper>> UpsertDuplicateResourceInternalAsync(
            RawResourceElement rawResourceElement,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(rawResourceElement, nameof(rawResourceElement));
            EnsureArg.IsNotNull(rawResourceElement.RawResource, nameof(rawResourceElement.RawResource));

            try
            {
                // Steps:
                // 1. Check if a source resource has one of the clinical reference code.
                //    (e.g. DiagnosticReport?code=[system|code]&subject=Patient/[id], DocumentReference?format=[system|code]&subject=Patient/[id])
                //    1.1. If it has, move to 2.
                //    1.2. If it hasn't, no actions.
                // 2. Search a duplicate resource(s) with the clinical reference code and subject (if specified).
                //    (e.g. DiagnosticReport?code=[system|code])
                //    2.1. If the search result has a resource(s), add missing attachemts in the source resport to it.
                //    2.2. If the search result has no resources, create a duplicate resource by copying the code, subject, and attachments.
                //         (Also, tag a duplicate resource so that we can identify a resource we created as a duplicate.)
                // 3. Return the duplicate resource(s) updated/created.
                var resource = ConvertToResource(rawResourceElement);
                var duplicateResourceWrappers = new List<ResourceWrapper>();
                if (ShouldDuplicate(resource))
                {
                    var resourceWrappersFound = await SearchDuplicateResourceAsync(
                        resource,
                        cancellationToken);
                    if (!resourceWrappersFound.Any())
                    {
                        var duplicateResourceWrapper = await CreateDuplicateResourceInternalAsync(
                            resource,
                            cancellationToken);
                        duplicateResourceWrappers.Add(duplicateResourceWrapper);
                    }
                    else
                    {
                        foreach (var wrapper in resourceWrappersFound)
                        {
                            var wrapperUpdated = await UpdateDuplicateResourceInternalAsync(
                                resource,
                                wrapper,
                                cancellationToken);
                            duplicateResourceWrappers.Add(wrapperUpdated);
                        }
                    }
                }
                else
                {
                    _logger.LogInformation("A resource doesn't have any attachment with clinical reference.");
                }

                return duplicateResourceWrappers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upsert a duplicate resource.");
                throw;
            }
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
                var diagnosticReport = (DiagnosticReport)resource;

                // TODO: more fields need to be populated?
                var documentReference = new DocumentReference
                {
                    Meta = new Meta
                    {
                        Tag = new List<Coding>
                        {
                            new Coding(TagDuplicateCreatedOn, DateTime.UtcNow.ToString("o")),
                            new Coding(TagDuplicateOf, diagnosticReport.Id),
                        },
                    },
                    Content = new List<DocumentReference.ContentComponent>(),
                    Subject = diagnosticReport.Subject,
#if R4 || R4B || Stu3
                    Status = DocumentReferenceStatus.Current,
#else
                    Status = DocumentReference.DocumentReferenceStatus.Current,
#endif
                };

                var crefs = GetClinicalReferences(diagnosticReport);
#if R4 || R4B || Stu3
                foreach (var cref in crefs)
                {
                    foreach (var code in cref.codes)
                    {
                        foreach (var attachment in cref.attachments)
                        {
                            documentReference.Content.Add(
                                new DocumentReference.ContentComponent
                                {
                                    Attachment = attachment,
                                    Format = code,
                                });
                        }
                    }
                }
#else
                foreach (var cref in crefs)
                {
                    var profiles = cref.codes
                        .Select(
                            x =>
                            {
                                return new DocumentReference.ProfileComponent()
                                {
                                    Value = x,
                                };
                            })
                        .ToList();
                    foreach (var attachment in cref.attachments)
                    {
                        documentReference.Content.Add(
                            new DocumentReference.ContentComponent
                            {
                                Attachment = attachment,
                                Profile = profiles,
                            });
                    }
                }
#endif

                return documentReference;
            }
            else
            {
                var documentReference = (DocumentReference)resource;

                // TODO: more fields need to be populated?
                var diagnosticReport = new DiagnosticReport
                {
                    Meta = new Meta
                    {
                        Tag = new List<Coding>
                        {
                            new Coding(TagDuplicateCreatedOn, DateTime.UtcNow.ToString("o")),
                            new Coding(TagDuplicateOf, resource.Id),
                        },
                    },
                    PresentedForm = new List<Hl7.Fhir.Model.Attachment>(),
                    Subject = documentReference.Subject,
                    Status = DiagnosticReport.DiagnosticReportStatus.Registered,
                };

                var crefs = GetClinicalReferences(documentReference);
                var codes = crefs.SelectMany(x => x.codes);
                diagnosticReport.Code = new CodeableConcept();
                foreach (var code in crefs.SelectMany(x => x.codes))
                {
                    diagnosticReport.Code.Add(code.System, code.Code, code.Display);
                }

                diagnosticReport.PresentedForm.AddRange(crefs.SelectMany(x => x.attachments));
                return diagnosticReport;
            }
        }

        private static List<Coding> GetClinicalReferenceCodes(Resource resource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            if (string.Equals(resource.TypeName, KnownResourceTypes.DiagnosticReport, StringComparison.OrdinalIgnoreCase))
            {
                var diagnosticReport = (DiagnosticReport)resource;
                return diagnosticReport.Code?.Coding?
                    .Where(x => ClinicalReferenceSystems.Contains(x?.System) && ClinicalReferenceCodes.Contains(x?.Code))
                    .DistinctBy(x => ClinicalReferenceDuplicatorHelper.ConvertToString(x))
                    .ToList();
            }
            else
            {
                var documentReference = (DocumentReference)resource;
#if R4 || R4B || Stu3
                return documentReference.Content
                    .Where(x => ClinicalReferenceSystems.Contains(x?.Format?.System) && ClinicalReferenceCodes.Contains(x?.Format?.Code))
                    .Select(x => x.Format)
                    .DistinctBy(x => ClinicalReferenceDuplicatorHelper.ConvertToString(x))
                    .ToList();
#else
                return documentReference.Content
                    .SelectMany(x => x?.Profile?
                        .Where(y => y?.Value?.GetType() == typeof(Coding)
                            && ClinicalReferenceSystems.Contains(((Coding)y.Value)?.System)
                            && ClinicalReferenceCodes.Contains(((Coding)y.Value)?.Code))
                        .Select(y => (Coding)y.Value))
                    .DistinctBy(x => ClinicalReferenceDuplicatorHelper.ConvertToString(x))
                    .ToList();
#endif
            }
        }

        private static List<(IList<Coding> codes, IList<Attachment> attachments)> GetClinicalReferences(Resource resource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var clinicalReferences = new List<(IList<Coding> codes, IList<Attachment> attachments)>();
            if (string.Equals(resource.TypeName, KnownResourceTypes.DiagnosticReport, StringComparison.OrdinalIgnoreCase))
            {
                var diagnosticReport = (DiagnosticReport)resource;
                var codes = diagnosticReport.Code?.Coding?
                    .Where(x => ClinicalReferenceSystems.Contains(x?.System) && ClinicalReferenceCodes.Contains(x?.Code))
                    .DistinctBy(x => ClinicalReferenceDuplicatorHelper.ConvertToString(x))
                    .ToList();
                clinicalReferences.Add(new(codes, diagnosticReport.PresentedForm));
            }
            else
            {
                var documentReference = (DocumentReference)resource;
#if R4 || R4B || Stu3
                var contents = documentReference.Content
                    .Where(x => ClinicalReferenceSystems.Contains(x?.Format?.System) && ClinicalReferenceCodes.Contains(x?.Format?.Code))
                    .DistinctBy(x => ClinicalReferenceDuplicatorHelper.ConvertToString(x.Format))
                    .ToList();
                foreach (var content in contents)
                {
                    clinicalReferences.Add(new(new List<Coding> { content.Format }, new List<Attachment> { content.Attachment }));
                }
#else
                foreach (var content in documentReference.Content)
                {
                    var codes = content.Profile
                        .Where(x => (x.Value is Coding)
                            && ClinicalReferenceSystems.Contains(((Coding)x.Value).System)
                            && ClinicalReferenceCodes.Contains(((Coding)x.Value).Code))
                        .Select(x => (Coding)x.Value)
                        .DistinctBy(x => ClinicalReferenceDuplicatorHelper.ConvertToString(x))
                        .ToList();
                    if (codes.Any())
                    {
                        clinicalReferences.Add(new(codes, new List<Attachment> { content.Attachment }));
                    }
                }
#endif
            }

            return clinicalReferences;
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

            var crefs = GetClinicalReferences(resource);
            return crefs.SelectMany(x => x.codes).Any() && crefs.SelectMany(x => x.attachments).Any();
        }

        private static bool UpdateDuplicateResource(
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

            var resourceUpdated = false;
            if (resource is DiagnosticReport)
            {
                var diagnosticReport = (DiagnosticReport)resource;
                var documentReference = (DocumentReference)duplicateResource;

#if R4 || R4B || Stu3
                foreach (var code in GetClinicalReferenceCodes(diagnosticReport))
                {
                    foreach (var attachment in diagnosticReport.PresentedForm)
                    {
                        if (!documentReference.Content.Any(
                                x => ClinicalReferenceDuplicatorHelper.CompareCoding(x.Format, code)
                                    && ClinicalReferenceDuplicatorHelper.CompareAttachment(x.Attachment, attachment)))
                        {
                            documentReference.Content.Add(
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = attachment,
                                    Format = code,
                                });
                            resourceUpdated = true;
                        }
                    }
                }
#else
                var profiles = GetClinicalReferenceCodes(diagnosticReport)
                    .Select(
                        x => new DocumentReference.ProfileComponent()
                        {
                            Value = new Coding(x.System, x.Code, x.Display),
                        })
                    .ToList();
                foreach (var attachment in diagnosticReport.PresentedForm)
                {
                    var contents = documentReference.Content
                        .Where(x => ClinicalReferenceDuplicatorHelper.CompareAttachment(x.Attachment, attachment))
                        .ToList();
                    if (!contents.Any())
                    {
                        documentReference.Content.Add(
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = attachment,
                                Profile = profiles,
                            });
                        resourceUpdated = true;
                        continue;
                    }

                    foreach (var content in contents)
                    {
                        if (!ClinicalReferenceDuplicatorHelper.CompareCodings(content.Profile, profiles))
                        {
                            content.Profile = content.Profile
                                .UnionBy(
                                    profiles,
                                    x => (x.Value is Coding) ? (((Coding)x.Value).System, ((Coding)x.Value).Code) : (string.Empty, string.Empty))
                                .ToList();
                            resourceUpdated = true;
                        }
                    }
                }
#endif
            }
            else
            {
                var documentReference = (DocumentReference)resource;
                var diagnosticReport = (DiagnosticReport)duplicateResource;
                var codes = GetClinicalReferenceCodes(diagnosticReport)
                    .Select(x => ClinicalReferenceDuplicatorHelper.ConvertToString(x))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var attachments = GetClinicalReferences(documentReference)
                    .Where(x => x.codes.Any(y => codes.Contains(ClinicalReferenceDuplicatorHelper.ConvertToString(y))))
                    .SelectMany(x => x.attachments)
                    .ToList();
                foreach (var attachment in attachments)
                {
                    if (!diagnosticReport.PresentedForm.Any(x => ClinicalReferenceDuplicatorHelper.CompareAttachment(x, attachment)))
                    {
                        diagnosticReport.PresentedForm.Add(attachment);
                        resourceUpdated = true;
                    }
                }
            }

            return resourceUpdated;
        }
    }
}
