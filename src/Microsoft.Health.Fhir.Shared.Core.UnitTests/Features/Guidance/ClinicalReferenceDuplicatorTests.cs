// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Guidance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Guidance
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class ClinicalReferenceDuplicatorTests
    {
        private readonly ClinicalReferenceDuplicator _duplicator;
        private readonly IFhirDataStore _dataStore;
        private readonly ISearchService _searchService;
        private readonly IRawResourceFactory _rawResourceFactory;
        private readonly IResourceWrapperFactory _resourceWrapperFactory;
        private readonly ILogger<ClinicalReferenceDuplicator> _logger;

        public ClinicalReferenceDuplicatorTests()
        {
            _dataStore = Substitute.For<IFhirDataStore>();
            _searchService = Substitute.For<ISearchService>();
            _logger = Substitute.For<ILogger<ClinicalReferenceDuplicator>>();

            _rawResourceFactory = Substitute.For<RawResourceFactory>(new FhirJsonSerializer());
            _resourceWrapperFactory = Substitute.For<IResourceWrapperFactory>();
            _resourceWrapperFactory
                .Create(Arg.Any<ResourceElement>(), Arg.Any<bool>(), Arg.Any<bool>())
                .Returns(x => CreateResourceWrapper(x.ArgAt<ResourceElement>(0), x.ArgAt<bool>(1)));

            _duplicator = new ClinicalReferenceDuplicator(
                _dataStore,
                _searchService,
                _resourceWrapperFactory,
                _logger);
        }

        [Theory]
        [MemberData(nameof(GetCreateResourceData))]
        public async Task GivenResource_WhenCreating_ThenDuplicateResourceShouldBeCreated(
            Resource resource,
            Resource duplicateResource,
            int searchCalls,
            int upsertCalls)
        {
            var resourceType = resource.TypeName;
            var duplicateResourceType = string.Equals(resourceType, KnownResourceTypes.DiagnosticReport)
                ? KnownResourceTypes.DocumentReference : KnownResourceTypes.DiagnosticReport;
            var codes = new List<Coding>();
            var subject = string.Empty;
            if (string.Equals(resourceType, KnownResourceTypes.DiagnosticReport))
            {
                var diagnosticReport = (DiagnosticReport)resource;
                subject = diagnosticReport.Subject?.Reference;
                codes.AddRange(diagnosticReport.Code.Coding
                    .Where(x => ClinicalReferenceDuplicator.ClinicalReferenceSystems.Contains(x.System)
                        && ClinicalReferenceDuplicator.ClinicalReferenceCodes.Contains(x.Code)));
            }
            else
            {
                var documentReference = (DocumentReference)resource;
                subject = documentReference.Subject?.Reference;
#if R4 || R4B || Stu3
                codes.AddRange(documentReference.Content
                    .Where(x => ClinicalReferenceDuplicator.ClinicalReferenceSystems.Contains(x.Format?.System)
                        && ClinicalReferenceDuplicator.ClinicalReferenceCodes.Contains(x.Format?.Code))
                    .Select(x => x.Format));
#else
                codes.AddRange(documentReference.Content
                    .SelectMany(x => x?.Profile?
                        .Where(y => y?.Value?.GetType() == typeof(Coding)
                            && ClinicalReferenceDuplicator.ClinicalReferenceSystems.Contains(((Coding)y.Value)?.System)
                            && ClinicalReferenceDuplicator.ClinicalReferenceCodes.Contains(((Coding)y.Value)?.Code))
                        .Select(y => (Coding)y.Value)));
#endif
            }

            // Set up a validation on a request for creating the source resource.
            var resourceElement = resource.ToResourceElement();
            _dataStore.UpsertAsync(
                Arg.Is<ResourceWrapperOperation>(x => string.Equals(x.Wrapper.ResourceTypeName, resourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>())
                .Throws(new Exception($"Shouldn't be called to create a {resourceType} resource."));

            // Set up a search result (no results for create).
            _searchService.SearchAsync(
                Arg.Is<string>(x => string.Equals(x, duplicateResourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>())
                .Returns(
                    x =>
                    {
                        Assert.True(searchCalls > 0, "SearchAsync shouldn't be called.");

                        var parameters = (IReadOnlyList<Tuple<string, string>>)x[1];
                        Assert.Contains(
                            parameters,
                            x => string.Equals(x.Item1, "subject", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(x.Item2, subject, StringComparison.OrdinalIgnoreCase));

                        if (string.Equals(duplicateResourceType, KnownResourceTypes.DiagnosticReport, StringComparison.OrdinalIgnoreCase))
                        {
                            Assert.Contains(
                                parameters,
                                x => string.Equals(x.Item1, "code", StringComparison.OrdinalIgnoreCase)
                                    && string.Equals(x.Item2, string.Join(",", codes.Select(x => ClinicalReferenceDuplicatorHelper.ConvertToString(x))), StringComparison.OrdinalIgnoreCase));
                        }
                        else
                        {
#if R4 || R4B || Stu3
                            Assert.Contains(
                                parameters,
                                x => string.Equals(x.Item1, "format", StringComparison.OrdinalIgnoreCase)
                                    && string.Equals(x.Item2, string.Join(",", codes.Select(x => ClinicalReferenceDuplicatorHelper.ConvertToString(x))), StringComparison.OrdinalIgnoreCase));
#else
                            Assert.Contains(
                                parameters,
                                x => string.Equals(x.Item1, "format-code", StringComparison.OrdinalIgnoreCase)
                                    && string.Equals(x.Item2, string.Join(",", codes.Select(x => ClinicalReferenceDuplicatorHelper.ConvertToString(x))), StringComparison.OrdinalIgnoreCase));
#endif
                        }

                        var searchResult = new SearchResult(
                            new List<SearchResultEntry>(),
                            null,
                            null,
                            new List<Tuple<string, string>>());
                        return Task.FromResult(searchResult);
                    });

            // Set up a validation on a request for creating a duplicate resource.
            _dataStore.UpsertAsync(
                Arg.Is<ResourceWrapperOperation>(x => string.Equals(x.Wrapper.ResourceTypeName, duplicateResourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>())
                .Returns(
                    x =>
                    {
                        Assert.True(upsertCalls > 0, "UpsertAsync shouldn't be called.");

                        var re = ((ResourceWrapperOperation)x[0])?.Wrapper?.RawResource?
                            .ToITypedElement(ModelInfoProvider.Instance)?
                            .ToResourceElement();
                        Assert.NotNull(re);
                        Assert.Equal(duplicateResourceType, re.InstanceType, true);

                        var r = re.ToPoco();
                        Assert.NotNull(r.Meta?.Tag);
                        Assert.Contains(
                            r.Meta.Tag,
                            x => string.Equals(x.System, ClinicalReferenceDuplicator.TagDuplicateOf, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(x.Code, resource.Id, StringComparison.OrdinalIgnoreCase));
                        Assert.Contains(
                            r.Meta.Tag,
                            x => string.Equals(x.System, ClinicalReferenceDuplicator.TagDuplicateCreatedOn, StringComparison.OrdinalIgnoreCase)
                                && DateTime.TryParse(x.Code, out _));

                        ValidateDuplicateResource(duplicateResource, r, true);
                        if (string.IsNullOrEmpty(r.Id))
                        {
                            r.Id = Guid.NewGuid().ToString();
                        }

                        return Task.FromResult(
                            new UpsertOutcome(
                                CreateResourceWrapper(r.ToResourceElement()),
                                SaveOutcomeType.Created));
                    });

            await _duplicator.CreateResourceAsync(
                new RawResourceElement(CreateResourceWrapper(resourceElement)),
                CancellationToken.None);

            // Check how many times these methods were called.
            await _searchService.Received(searchCalls).SearchAsync(
                Arg.Is<string>(x => string.Equals(x, duplicateResourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>());
            await _searchService.Received(0).SearchAsync(
                Arg.Is<string>(x => string.Equals(x, resourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>());
            await _dataStore.Received(upsertCalls).UpsertAsync(
                Arg.Is<ResourceWrapperOperation>(x => string.Equals(x.Wrapper.ResourceTypeName, duplicateResourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>());
            await _dataStore.Received(0).UpsertAsync(
                Arg.Is<ResourceWrapperOperation>(x => string.Equals(x.Wrapper.ResourceTypeName, resourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [MemberData(nameof(GetUpdateResourceData))]
        public async Task GivenResource_WhenUpdating_ThenDuplicateResourceShouldBeUpdated(
            Resource resource,
            List<Resource> duplicateResources,
            List<Resource> searchResults,
            int searchCalls,
            int upsertCalls)
        {
            var resourceType = resource.TypeName;
            var duplicateResourceType = string.Equals(resourceType, KnownResourceTypes.DiagnosticReport)
                ? KnownResourceTypes.DocumentReference : KnownResourceTypes.DiagnosticReport;
            var codes = new List<Coding>();
            var subject = string.Empty;
            if (string.Equals(resourceType, KnownResourceTypes.DiagnosticReport))
            {
                var diagnosticReport = (DiagnosticReport)resource;
                subject = diagnosticReport.Subject?.Reference;
                codes.AddRange(diagnosticReport.Code.Coding
                    .Where(x => ClinicalReferenceDuplicator.ClinicalReferenceSystems.Contains(x.System)
                        && ClinicalReferenceDuplicator.ClinicalReferenceCodes.Contains(x.Code)));
            }
            else
            {
                var documentReference = (DocumentReference)resource;
                subject = documentReference.Subject?.Reference;
#if R4 || R4B || Stu3
                codes.AddRange(documentReference.Content
                    .Where(x => ClinicalReferenceDuplicator.ClinicalReferenceSystems.Contains(x.Format?.System)
                        && ClinicalReferenceDuplicator.ClinicalReferenceCodes.Contains(x.Format?.Code))
                    .Select(x => x.Format));
#else
                codes.AddRange(documentReference.Content
                    .SelectMany(x => x?.Profile?
                        .Where(y => y?.Value?.GetType() == typeof(Coding)
                            && ClinicalReferenceDuplicator.ClinicalReferenceSystems.Contains(((Coding)y.Value)?.System)
                            && ClinicalReferenceDuplicator.ClinicalReferenceCodes.Contains(((Coding)y.Value)?.Code))
                        .Select(y => (Coding)y.Value)));
#endif
            }

            // Set up a validation on a request for searching duplicate resources.
            var entries = new List<SearchResultEntry>();
            foreach (var r in searchResults)
            {
                var wrapper = new ResourceWrapper(
                    r.ToResourceElement(),
                    new RawResource(r.ToJson(), FhirResourceFormat.Json, false),
                    null,
                    false,
                    null,
                    null,
                    null);
                entries.Add(new SearchResultEntry(wrapper));
            }

            _searchService.SearchAsync(
                Arg.Is<string>(x => string.Equals(x, duplicateResourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>())
                .Returns(
                    x =>
                    {
                        Assert.True(searchCalls > 0, "SearchAsync shouldn't be called.");

                        var parameters = (IReadOnlyList<Tuple<string, string>>)x[1];
                        Assert.Contains(
                            parameters,
                            x => string.Equals(x.Item1, "subject", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(x.Item2, subject, StringComparison.OrdinalIgnoreCase));

                        if (string.Equals(duplicateResourceType, KnownResourceTypes.DiagnosticReport, StringComparison.OrdinalIgnoreCase))
                        {
                            Assert.Contains(
                                parameters,
                                x => string.Equals(x.Item1, "code", StringComparison.OrdinalIgnoreCase)
                                    && string.Equals(x.Item2, string.Join(",", codes.Select(x => ClinicalReferenceDuplicatorHelper.ConvertToString(x))), StringComparison.OrdinalIgnoreCase));
                        }
                        else
                        {
#if R4 || R4B || Stu3
                            Assert.Contains(
                                parameters,
                                x => string.Equals(x.Item1, "format", StringComparison.OrdinalIgnoreCase)
                                    && string.Equals(x.Item2, string.Join(",", codes.Select(x => ClinicalReferenceDuplicatorHelper.ConvertToString(x))), StringComparison.OrdinalIgnoreCase));
#else
                            Assert.Contains(
                                parameters,
                                x => string.Equals(x.Item1, "format-code", StringComparison.OrdinalIgnoreCase)
                                    && string.Equals(x.Item2, string.Join(",", codes.Select(x => ClinicalReferenceDuplicatorHelper.ConvertToString(x))), StringComparison.OrdinalIgnoreCase));
#endif
                        }

                        return Task.FromResult(
                            new SearchResult(
                                entries,
                                null,
                                null,
                                new List<Tuple<string, string>>()));
                    });

            // Set up a validation on a request for creating a duplicate resource.
            _dataStore.UpsertAsync(
                Arg.Is<ResourceWrapperOperation>(x => string.Equals(x.Wrapper.ResourceTypeName, duplicateResourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>())
                .Returns(
                    x =>
                    {
                        Assert.True(upsertCalls > 0, "UpsertAsync shouldn't be called.");

                        var re = ((ResourceWrapperOperation)x[0])?.Wrapper?.RawResource?
                            .ToITypedElement(ModelInfoProvider.Instance)?
                            .ToResourceElement();
                        Assert.NotNull(re);
                        Assert.Equal(duplicateResourceType, re.InstanceType, true);

                        var r = re.ToPoco();
                        Assert.NotNull(r);

                        ValidateDuplicateResource(
                            duplicateResources.Where(x => string.Equals(x.Id, r.Id, StringComparison.OrdinalIgnoreCase)).First(),
                            r);
                        if (string.IsNullOrEmpty(r.Id))
                        {
                            r.Id = Guid.NewGuid().ToString();
                        }

                        return Task.FromResult(
                            new UpsertOutcome(
                                CreateResourceWrapper(r.ToResourceElement()),
                                SaveOutcomeType.Created));
                    });

            await _duplicator.UpdateResourceAsync(
                new RawResourceElement(CreateResourceWrapper(resource.ToResourceElement())),
                CancellationToken.None);

            // Check how many times these methods were called.
            await _searchService.Received(searchCalls).SearchAsync(
                Arg.Is<string>(x => string.Equals(x, duplicateResourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>());
            await _searchService.Received(0).SearchAsync(
                Arg.Is<string>(x => string.Equals(x, resourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>());
            await _dataStore.Received(upsertCalls).UpsertAsync(
                Arg.Is<ResourceWrapperOperation>(x => string.Equals(x.Wrapper.ResourceTypeName, duplicateResourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>());
            await _dataStore.Received(0).UpsertAsync(
                Arg.Is<ResourceWrapperOperation>(x => string.Equals(x.Wrapper.ResourceTypeName, resourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [MemberData(nameof(GetDeleteResourceData))]
        public async Task GivenResource_WhenDeleting_ThenDuplicateResourceShouldBeDeleted(
            Resource resource,
            List<Resource> duplicateResources,
            DeleteOperation deleteOperation,
            int searchCalls,
            int deleteCalls)
        {
            var resourceType = resource.TypeName;
            var duplicateResourceType = string.Equals(resourceType, KnownResourceTypes.DiagnosticReport)
                ? KnownResourceTypes.DocumentReference : KnownResourceTypes.DiagnosticReport;
            var duplicateResourceIds = duplicateResources.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Set up a validation on a request for searching duplicate resources.
            var entries = new List<SearchResultEntry>();
            if (duplicateResources?.Any() ?? false)
            {
                foreach (var r in duplicateResources)
                {
                    var wrapper = new ResourceWrapper(
                        r.ToResourceElement(),
                        new RawResource(r.ToJson(), FhirResourceFormat.Json, false),
                        null,
                        false,
                        null,
                        null,
                        null);
                    entries.Add(new SearchResultEntry(wrapper));
                }
            }

            _searchService.SearchAsync(
                Arg.Is<string>(x => string.Equals(x, duplicateResourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>())
                .Returns(
                    x =>
                    {
                        Assert.True(searchCalls > 0, "SearchAsync shouldn't be called.");

                        var parameters = (IReadOnlyList<Tuple<string, string>>)x[1];
                        Assert.Contains(
                            parameters,
                            x => string.Equals(x.Item1, "_tag", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(x.Item2, $"{ClinicalReferenceDuplicator.TagDuplicateOf}|{resource.Id}", StringComparison.OrdinalIgnoreCase));

                        var searchResult = new SearchResult(
                            entries,
                            null,
                            null,
                            new List<Tuple<string, string>>());
                        return Task.FromResult(searchResult);
                    });

            // Set up a validation on a request for soft-deleting the duplicate resource.
            _dataStore.UpsertAsync(
                Arg.Is<ResourceWrapperOperation>(x => string.Equals(x.Wrapper.ResourceTypeName, duplicateResourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>())
                .Returns(
                    x =>
                    {
                        Assert.True(deleteCalls > 0, "Upsert shouldn't be called.");

                        var r = ((ResourceWrapperOperation)x[0])?.Wrapper;
                        Assert.NotNull(r);
                        Assert.Equal(duplicateResourceType, r.ResourceTypeName, true);
                        Assert.Contains(r.ResourceId, duplicateResourceIds);

                        return Task.FromResult(
                            new UpsertOutcome(r, SaveOutcomeType.Updated));
                    });

            // Set up a validation on a request for hard-deleting the duplicate resource.
            _dataStore.HardDeleteAsync(
                Arg.Is<ResourceKey>(x => string.Equals(x.ResourceType, duplicateResourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
                .Returns(
                    x =>
                    {
                        Assert.True(deleteCalls > 0, "HardDelete shouldn't be called.");

                        var k = (ResourceKey)x[0];
                        Assert.NotNull(k);
                        Assert.Equal(duplicateResourceType, k.ResourceType, true);
                        Assert.Contains(k.Id, duplicateResourceIds);

                        return Task.CompletedTask;
                    });

            await _duplicator.DeleteResourceAsync(
                new ResourceKey(resource.TypeName, resource.Id),
                deleteOperation,
                CancellationToken.None);

            // Check how many times create/update was invoked.
            await _searchService.Received(searchCalls).SearchAsync(
                Arg.Is<string>(x => string.Equals(x, duplicateResourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>());
            await _dataStore.Received(deleteOperation == DeleteOperation.SoftDelete ? deleteCalls : 0).UpsertAsync(
                Arg.Is<ResourceWrapperOperation>(x => string.Equals(x.Wrapper.ResourceTypeName, duplicateResourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>());
            await _dataStore.Received(deleteOperation == DeleteOperation.HardDelete ? deleteCalls : 0).HardDeleteAsync(
                Arg.Is<ResourceKey>(x => string.Equals(x.ResourceType, duplicateResourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>());
        }

        private ResourceWrapper CreateResourceWrapper(ResourceElement resource, bool isDeleted = false)
        {
            return new ResourceWrapper(
                resource,
                _rawResourceFactory.Create(resource, keepMeta: true),
                new ResourceRequest(HttpMethod.Post, "http://fhir"),
                isDeleted,
                null,
                null,
                null,
                null,
                0);
        }

        private static void ValidateDuplicateResource(Resource expected, Resource actual, bool ignoreId = false)
        {
            EnsureArg.IsNotNull(expected, nameof(expected));
            EnsureArg.IsNotNull(actual, nameof(actual));

            if (!ignoreId)
            {
                Assert.Equal(expected.Id, actual.Id);
            }

            Assert.Equal(expected.TypeName, actual.TypeName);
            if (string.Equals(expected.TypeName, KnownResourceTypes.DiagnosticReport, StringComparison.OrdinalIgnoreCase))
            {
                var expectedResource = (DiagnosticReport)expected;
                var actualResource = (DiagnosticReport)actual;

                Assert.Equal(expectedResource.Subject?.Reference, actualResource.Subject?.Reference);
                Assert.True(ClinicalReferenceDuplicatorHelper.CompareCodings(expectedResource.Code?.Coding, actualResource.Code?.Coding));
                Assert.True(ClinicalReferenceDuplicatorHelper.CompareAttachments(expectedResource.PresentedForm, actualResource.PresentedForm));
            }
            else
            {
                var expectedResource = (DocumentReference)expected;
                var actualResource = (DocumentReference)actual;

                Assert.Equal(expectedResource.Subject?.Reference, actualResource.Subject?.Reference);
                Assert.True(ClinicalReferenceDuplicatorHelper.CompareContents(expectedResource.Content, actualResource.Content));
            }
        }

        public static IEnumerable<object[]> GetCreateResourceData()
        {
            var data = new[]
            {
                new object[]
                {
                    // Create a new DiagnosticReport resource with one attachment.
                    new DiagnosticReport
                    {
                        Id = Guid.NewGuid().ToString(),
                        Status = DiagnosticReport.DiagnosticReportStatus.Registered,
                        Code = new CodeableConcept()
                        {
                            Coding = new List<Coding>()
                            {
                                new Coding()
                                {
                                    Code = "11488-4",
                                    System = "https://loinc.org",
                                },
                            },
                        },
                        Subject = new ResourceReference("patient"),
                        PresentedForm = new List<Attachment>
                        {
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2005-12-24",
                                Url = "http://example.org/fhir/Binary/attachment",
                            },
                        },
                    },
                    new DocumentReference
                    {
                        Id = Guid.NewGuid().ToString(),
                        Content = new List<DocumentReference.ContentComponent>
                        {
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2005-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment",
                                },
#if R4 || R4B || Stu3
                                Format = new Coding()
                                {
                                    Code = "11488-4",
                                    System = "https://loinc.org",
                                },
#else
                                Profile = new List<DocumentReference.ProfileComponent>
                                {
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding()
                                        {
                                            Code = "11488-4",
                                            System = "https://loinc.org",
                                        },
                                    },
                                },
#endif
                            },
                        },
                        Subject = new ResourceReference("patient"),
#if R4 || R4B || Stu3
                        Status = DocumentReferenceStatus.Current,
#else
                        Status = DocumentReference.DocumentReferenceStatus.Current,
#endif
                    },
                    1,
                    1,
                },
                new object[]
                {
                    // Create a new DiagnosticReport resource with multiple codes and attachments.
                    new DiagnosticReport
                    {
                        Id = Guid.NewGuid().ToString(),
                        Status = DiagnosticReport.DiagnosticReportStatus.Registered,
                        Code = new CodeableConcept()
                        {
                            Coding = new List<Coding>()
                            {
                                new Coding()
                                {
                                    Code = "12345-1",
                                    System = "https://loinc.org",
                                },
                                new Coding()
                                {
                                    Code = "11488-4",
                                    System = "https://loinc.org",
                                },
                                new Coding()
                                {
                                    Code = "12345-2",
                                    System = "https://loinc.org",
                                },
                                new Coding()
                                {
                                    Code = "11502-2",
                                    System = "https://loinc.org",
                                },
                            },
                        },
                        Subject = new ResourceReference("patient"),
                        PresentedForm = new List<Attachment>
                        {
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2005-12-24",
                                Url = "http://example.org/fhir/Binary/attachment",
                            },
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2006-12-24",
                                Url = "http://example.org/fhir/Binary/attachment1",
                            },
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2007-12-24",
                                Url = "http://example.org/fhir/Binary/attachment2",
                            },
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2008-12-24",
                                Url = "http://example.org/fhir/Binary/attachment3",
                            },
                        },
                    },
                    new DocumentReference
                    {
                        Id = Guid.NewGuid().ToString(),
                        Content = new List<DocumentReference.ContentComponent>
                        {
#if R4 || R4B || Stu3
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2005-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment",
                                },
                                Format = new Coding()
                                {
                                    Code = "11488-4",
                                    System = "https://loinc.org",
                                },
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2006-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment1",
                                },
                                Format = new Coding()
                                {
                                    Code = "11488-4",
                                    System = "https://loinc.org",
                                },
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2007-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment2",
                                },
                                Format = new Coding()
                                {
                                    Code = "11488-4",
                                    System = "https://loinc.org",
                                },
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2008-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment3",
                                },
                                Format = new Coding()
                                {
                                    Code = "11488-4",
                                    System = "https://loinc.org",
                                },
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2005-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment",
                                },
                                Format = new Coding()
                                {
                                    Code = "11502-2",
                                    System = "https://loinc.org",
                                },
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2006-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment1",
                                },
                                Format = new Coding()
                                {
                                    Code = "11502-2",
                                    System = "https://loinc.org",
                                },
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2007-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment2",
                                },
                                Format = new Coding()
                                {
                                    Code = "11502-2",
                                    System = "https://loinc.org",
                                },
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2008-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment3",
                                },
                                Format = new Coding()
                                {
                                    Code = "11502-2",
                                    System = "https://loinc.org",
                                },
                            },
#else
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2005-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment",
                                },
                                Profile = new List<DocumentReference.ProfileComponent>
                                {
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding()
                                        {
                                            Code = "11488-4",
                                            System = "https://loinc.org",
                                        },
                                    },
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding()
                                        {
                                            Code = "11502-2",
                                            System = "https://loinc.org",
                                        },
                                    },
                                },
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2006-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment1",
                                },
                                Profile = new List<DocumentReference.ProfileComponent>
                                {
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding()
                                        {
                                            Code = "11488-4",
                                            System = "https://loinc.org",
                                        },
                                    },
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding()
                                        {
                                            Code = "11502-2",
                                            System = "https://loinc.org",
                                        },
                                    },
                                },
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2007-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment2",
                                },
                                Profile = new List<DocumentReference.ProfileComponent>
                                {
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding()
                                        {
                                            Code = "11488-4",
                                            System = "https://loinc.org",
                                        },
                                    },
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding()
                                        {
                                            Code = "11502-2",
                                            System = "https://loinc.org",
                                        },
                                    },
                                },
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2008-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment3",
                                },
                                Profile = new List<DocumentReference.ProfileComponent>
                                {
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding()
                                        {
                                            Code = "11488-4",
                                            System = "https://loinc.org",
                                        },
                                    },
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding()
                                        {
                                            Code = "11502-2",
                                            System = "https://loinc.org",
                                        },
                                    },
                                },
                            },
#endif
                        },
                        Subject = new ResourceReference("patient"),
#if R4 || R4B || Stu3
                        Status = DocumentReferenceStatus.Current,
#else
                        Status = DocumentReference.DocumentReferenceStatus.Current,
#endif
                    },
                    1,
                    1,
                },
                new object[]
                {
                    // Create a new DiagnosticReport resource without any attachment.
                    new DiagnosticReport
                    {
                        Id = Guid.NewGuid().ToString(),
                        Status = DiagnosticReport.DiagnosticReportStatus.Registered,
                        Code = new CodeableConcept()
                        {
                            Coding = new List<Coding>()
                            {
                                new Coding()
                                {
                                    Code = "11488-4",
                                    System = "https://loinc.org",
                                },
                            },
                        },
                        Subject = new ResourceReference("patient"),
                    },
                    null,
                    0,
                    0,
                },
                new object[]
                {
                    // Create a new DiagnosticReport resource without clinical reference codes.
                    new DiagnosticReport
                    {
                        Id = Guid.NewGuid().ToString(),
                        Status = DiagnosticReport.DiagnosticReportStatus.Registered,
                        Code = new CodeableConcept()
                        {
                            Coding = new List<Coding>()
                            {
                                new Coding()
                                {
                                    Code = "12345-1",
                                    System = "https://loinc.org",
                                },
                            },
                        },
                        Subject = new ResourceReference("patient"),
                        PresentedForm = new List<Attachment>
                        {
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2005-12-24",
                                Url = "http://example.org/fhir/Binary/attachment",
                            },
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2006-12-24",
                                Url = "http://example.org/fhir/Binary/attachment1",
                            },
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2007-12-24",
                                Url = "http://example.org/fhir/Binary/attachment2",
                            },
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2005-12-24",
                                Url = "http://example.org/fhir/Binary/attachment3",
                            },
                        },
                    },
                    null,
                    0,
                    0,
                },
                new object[]
                {
                    // Create a new DocumentReference resource with one attachment.
                    new DocumentReference
                    {
                        Id = Guid.NewGuid().ToString(),
                        Content = new List<DocumentReference.ContentComponent>
                        {
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2005-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment",
                                },
#if R4 || R4B || Stu3
                                Format = new Coding()
                                {
                                    Code = "11488-4",
                                    System = "https://loinc.org",
                                },
#else
                                Profile = new List<DocumentReference.ProfileComponent>
                                {
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding()
                                        {
                                            Code = "11488-4",
                                            System = "https://loinc.org",
                                        },
                                    },
                                },
#endif
                            },
                        },
                        Subject = new ResourceReference("patient"),
#if R4 || R4B || Stu3
                        Status = DocumentReferenceStatus.Current,
#else
                        Status = DocumentReference.DocumentReferenceStatus.Current,
#endif
                    },
                    new DiagnosticReport
                    {
                        Id = Guid.NewGuid().ToString(),
                        Status = DiagnosticReport.DiagnosticReportStatus.Registered,
                        Code = new CodeableConcept()
                        {
                            Coding = new List<Coding>()
                            {
                                new Coding()
                                {
                                    Code = "11488-4",
                                    System = "https://loinc.org",
                                },
                            },
                        },
                        Subject = new ResourceReference("patient"),
                        PresentedForm = new List<Attachment>
                        {
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2005-12-24",
                                Url = "http://example.org/fhir/Binary/attachment",
                            },
                        },
                    },
                    1,
                    1,
                },
                new object[]
                {
                    // Create a new DocumentReference resource with multiple attachments.
                    new DocumentReference
                    {
                        Id = Guid.NewGuid().ToString(),
                        Content = new List<DocumentReference.ContentComponent>
                        {
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2005-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment",
                                },
#if R4 || R4B || Stu3
                                Format = new Coding()
                                {
                                    Code = "11488-4",
                                    System = "https://loinc.org",
                                },
#else
                                Profile = new List<DocumentReference.ProfileComponent>
                                {
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding()
                                        {
                                            Code = "11488-4",
                                            System = "https://loinc.org",
                                        },
                                    },
                                },
#endif
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2006-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment1",
                                },
#if R4 || R4B || Stu3
                                Format = new Coding()
                                {
                                    Code = "12345-1",
                                    System = "https://loinc.org",
                                },
#else
                                Profile = new List<DocumentReference.ProfileComponent>
                                {
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding()
                                        {
                                            Code = "12345-1",
                                            System = "https://loinc.org",
                                        },
                                    },
                                },
#endif
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2007-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment2",
                                },
#if R4 || R4B || Stu3
                                Format = new Coding()
                                {
                                    Code = "18748-4",
                                    System = "https://loinc.org",
                                },
#else
                                Profile = new List<DocumentReference.ProfileComponent>
                                {
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding()
                                        {
                                            Code = "18748-4",
                                            System = "https://loinc.org",
                                        },
                                    },
                                },
#endif
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2005-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment3",
                                },
#if R4 || R4B || Stu3
                                Format = new Coding()
                                {
                                    Code = "12345-2",
                                    System = "https://loinc.org",
                                },
#else
                                Profile = new List<DocumentReference.ProfileComponent>
                                {
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding()
                                        {
                                            Code = "12345-2",
                                            System = "https://loinc.org",
                                        },
                                    },
                                },
#endif
                            },
                        },
                        Subject = new ResourceReference("patient"),
#if R4 || R4B || Stu3
                        Status = DocumentReferenceStatus.Current,
#else
                        Status = DocumentReference.DocumentReferenceStatus.Current,
#endif
                    },
                    new DiagnosticReport
                    {
                        Id = Guid.NewGuid().ToString(),
                        Status = DiagnosticReport.DiagnosticReportStatus.Registered,
                        Code = new CodeableConcept()
                        {
                            Coding = new List<Coding>()
                            {
                                new Coding()
                                {
                                    Code = "11488-4",
                                    System = "https://loinc.org",
                                },
                                new Coding()
                                {
                                    Code = "18748-4",
                                    System = "https://loinc.org",
                                },
                            },
                        },
                        Subject = new ResourceReference("patient"),
                        PresentedForm = new List<Attachment>
                        {
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2005-12-24",
                                Url = "http://example.org/fhir/Binary/attachment",
                            },
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2007-12-24",
                                Url = "http://example.org/fhir/Binary/attachment2",
                            },
                        },
                    },
                    1,
                    1,
                },
                new object[]
                {
                    // Create a new DocumentReference resource without any attachment with clinical reference.
                    new DocumentReference
                    {
                        Id = Guid.NewGuid().ToString(),
                        Content = new List<DocumentReference.ContentComponent>
                        {
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2006-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment",
                                },
#if R4 || R4B || Stu3
                                Format = new Coding()
                                {
                                    Code = "12345-1",
                                    System = "https://loinc.org",
                                },
#else
                                Profile = new List<DocumentReference.ProfileComponent>
                                {
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding()
                                        {
                                            Code = "12345-1",
                                            System = "https://loinc.org",
                                        },
                                    },
                                },
#endif
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2006-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment1",
                                },
#if R4 || R4B || Stu3
                                Format = new Coding()
                                {
                                    Code = "12345-2",
                                    System = "https://loinc.org",
                                },
#else
                                Profile = new List<DocumentReference.ProfileComponent>
                                {
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding()
                                        {
                                            Code = "12345-2",
                                            System = "https://loinc.org",
                                        },
                                    },
                                },
#endif
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2006-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment2",
                                },
#if R4 || R4B || Stu3
                                Format = new Coding()
                                {
                                    Code = "12345-3",
                                    System = "https://loinc.org",
                                },
#else
                                Profile = new List<DocumentReference.ProfileComponent>
                                {
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding()
                                        {
                                            Code = "12345-3",
                                            System = "https://loinc.org",
                                        },
                                    },
                                },
#endif
                            },
                        },
                        Subject = new ResourceReference("patient"),
#if R4 || R4B || Stu3
                        Status = DocumentReferenceStatus.Current,
#else
                        Status = DocumentReference.DocumentReferenceStatus.Current,
#endif
                    },
                    null,
                    0,
                    0,
                },
            };

            foreach (var d in data)
            {
                yield return d;
            }
        }

        public static IEnumerable<object[]> GetUpdateResourceData()
        {
            var data = new[]
            {
                new object[]
                {
                    // Update a new DiagnosticReport resource with one attachment.
                    new DiagnosticReport
                    {
                        Id = Guid.NewGuid().ToString(),
                        Status = DiagnosticReport.DiagnosticReportStatus.Registered,
                        Code = new CodeableConcept()
                        {
                            Coding = new List<Coding>()
                            {
                                new Coding()
                                {
                                    Code = "11488-4",
                                    System = "https://loinc.org",
                                },
                            },
                        },
                        Subject = new ResourceReference("patient"),
                        PresentedForm = new List<Attachment>
                        {
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2005-12-24",
                                Url = "http://example.org/fhir/source/attachment",
                            },
                        },
                    },
                    new List<Resource>
                    {
                        new DocumentReference
                        {
                            Id = "duplicate",
                            Content = new List<DocumentReference.ContentComponent>
                            {
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2005-12-24",
                                        Url = "http://example.org/fhir/duplicate/attachment",
                                    },
#if R4 || R4B || Stu3
                                    Format = new Coding()
                                    {
                                        Code = "11488-4",
                                        System = "https://loinc.org",
                                    },
#else
                                    Profile = new List<DocumentReference.ProfileComponent>
                                    {
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "11488-4",
                                                System = "https://loinc.org",
                                            },
                                        },
                                    },
#endif
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2005-12-24",
                                        Url = "http://example.org/fhir/source/attachment",
                                    },
#if R4 || R4B || Stu3
                                    Format = new Coding()
                                    {
                                        Code = "11488-4",
                                        System = "https://loinc.org",
                                    },
#else
                                    Profile = new List<DocumentReference.ProfileComponent>
                                    {
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "11488-4",
                                                System = "https://loinc.org",
                                            },
                                        },
                                    },
#endif
                                },
                            },
                            Subject = new ResourceReference("patient"),
#if R4 || R4B || Stu3
                            Status = DocumentReferenceStatus.Current,
#else
                            Status = DocumentReference.DocumentReferenceStatus.Current,
#endif
                        },
                    },
                    new List<Resource>
                    {
                        new DocumentReference
                        {
                            Id = "duplicate",
                            Content = new List<DocumentReference.ContentComponent>
                            {
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2005-12-24",
                                        Url = "http://example.org/fhir/duplicate/attachment",
                                    },
#if R4 || R4B || Stu3
                                    Format = new Coding()
                                    {
                                        Code = "11488-4",
                                        System = "https://loinc.org",
                                    },
#else
                                    Profile = new List<DocumentReference.ProfileComponent>
                                    {
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "11488-4",
                                                System = "https://loinc.org",
                                            },
                                        },
                                    },
#endif
                                },
                            },
                            Subject = new ResourceReference("patient"),
#if R4 || R4B || Stu3
                            Status = DocumentReferenceStatus.Current,
#else
                            Status = DocumentReference.DocumentReferenceStatus.Current,
#endif
                        },
                    },
                    1,
                    1,
                },
                new object[]
                {
                    // Update a new DiagnosticReport resource with multiple codes and attachments.
                    new DiagnosticReport
                    {
                        Id = Guid.NewGuid().ToString(),
                        Status = DiagnosticReport.DiagnosticReportStatus.Registered,
                        Code = new CodeableConcept()
                        {
                            Coding = new List<Coding>()
                            {
                                new Coding()
                                {
                                    Code = "11488-4",
                                    System = "https://loinc.org",
                                },
                                new Coding()
                                {
                                    Code = "12345-3",
                                    System = "https://loinc.org",
                                },
                                new Coding()
                                {
                                    Code = "34117-2",
                                    System = "https://loinc.org",
                                },
                            },
                        },
                        Subject = new ResourceReference("patient"),
                        PresentedForm = new List<Attachment>
                        {
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2005-12-24",
                                Url = "http://example.org/fhir/source/attachment",
                            },
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2006-12-24",
                                Url = "http://example.org/fhir/source/attachment1",
                            },
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2007-12-24",
                                Url = "http://example.org/fhir/source/attachment2",
                            },
                        },
                    },
                    new List<Resource>
                    {
                        new DocumentReference
                        {
                            Id = "duplicate",
                            Content = new List<DocumentReference.ContentComponent>
                            {
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2005-12-24",
                                        Url = "http://example.org/fhir/duplicate/attachment",
                                    },
#if R4 || R4B || Stu3
                                    Format = new Coding()
                                    {
                                        Code = "11488-4",
                                        System = "https://loinc.org",
                                    },
#else
                                    Profile = new List<DocumentReference.ProfileComponent>
                                    {
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "11488-4",
                                                System = "https://loinc.org",
                                            },
                                        },
                                    },
#endif
                                },
#if R4 || R4B || Stu3
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2005-12-24",
                                        Url = "http://example.org/fhir/source/attachment",
                                    },
                                    Format = new Coding()
                                    {
                                        Code = "11488-4",
                                        System = "https://loinc.org",
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2006-12-24",
                                        Url = "http://example.org/fhir/source/attachment1",
                                    },
                                    Format = new Coding()
                                    {
                                        Code = "11488-4",
                                        System = "https://loinc.org",
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2007-12-24",
                                        Url = "http://example.org/fhir/source/attachment2",
                                    },
                                    Format = new Coding()
                                    {
                                        Code = "11488-4",
                                        System = "https://loinc.org",
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2005-12-24",
                                        Url = "http://example.org/fhir/source/attachment",
                                    },
                                    Format = new Coding()
                                    {
                                        Code = "34117-2",
                                        System = "https://loinc.org",
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2006-12-24",
                                        Url = "http://example.org/fhir/source/attachment1",
                                    },
                                    Format = new Coding()
                                    {
                                        Code = "34117-2",
                                        System = "https://loinc.org",
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2007-12-24",
                                        Url = "http://example.org/fhir/source/attachment2",
                                    },
                                    Format = new Coding()
                                    {
                                        Code = "34117-2",
                                        System = "https://loinc.org",
                                    },
                                },
#else
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2005-12-24",
                                        Url = "http://example.org/fhir/source/attachment",
                                    },
                                    Profile = new List<DocumentReference.ProfileComponent>
                                    {
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "11488-4",
                                                System = "https://loinc.org",
                                            },
                                        },
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "34117-2",
                                                System = "https://loinc.org",
                                            },
                                        },
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2006-12-24",
                                        Url = "http://example.org/fhir/source/attachment1",
                                    },
                                    Profile = new List<DocumentReference.ProfileComponent>
                                    {
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "11488-4",
                                                System = "https://loinc.org",
                                            },
                                        },
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "34117-2",
                                                System = "https://loinc.org",
                                            },
                                        },
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2007-12-24",
                                        Url = "http://example.org/fhir/source/attachment2",
                                    },
                                    Profile = new List<DocumentReference.ProfileComponent>
                                    {
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "11488-4",
                                                System = "https://loinc.org",
                                            },
                                        },
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "34117-2",
                                                System = "https://loinc.org",
                                            },
                                        },
                                    },
                                },
#endif
                            },
                            Subject = new ResourceReference("patient"),
#if R4 || R4B || Stu3
                            Status = DocumentReferenceStatus.Current,
#else
                            Status = DocumentReference.DocumentReferenceStatus.Current,
#endif
                        },
                    },
                    new List<Resource>
                    {
                        new DocumentReference
                        {
                            Id = "duplicate",
                            Content = new List<DocumentReference.ContentComponent>
                            {
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2005-12-24",
                                        Url = "http://example.org/fhir/duplicate/attachment",
                                    },
#if R4 || R4B || Stu3
                                    Format = new Coding()
                                    {
                                        Code = "11488-4",
                                        System = "https://loinc.org",
                                    },
#else
                                    Profile = new List<DocumentReference.ProfileComponent>
                                    {
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "11488-4",
                                                System = "https://loinc.org",
                                            },
                                        },
                                    },
#endif
                                },
                            },
                            Subject = new ResourceReference("patient"),
#if R4 || R4B || Stu3
                            Status = DocumentReferenceStatus.Current,
#else
                            Status = DocumentReference.DocumentReferenceStatus.Current,
#endif
                        },
                    },
                    1,
                    1,
                },
                new object[]
                {
                    // Update a new DiagnosticReport resource without any attachment.
                    new DiagnosticReport
                    {
                        Id = Guid.NewGuid().ToString(),
                        Status = DiagnosticReport.DiagnosticReportStatus.Registered,
                        Code = new CodeableConcept()
                        {
                            Coding = new List<Coding>()
                            {
                                new Coding()
                                {
                                    Code = "11488-4",
                                    System = "https://loinc.org",
                                },
                            },
                        },
                        Subject = new ResourceReference("patient"),
                    },
                    new List<Resource>(),
                    new List<Resource>(),
                    0,
                    0,
                },
                new object[]
                {
                    // Update a new DiagnosticReport resource without any clinical reference code.
                    new DiagnosticReport
                    {
                        Id = Guid.NewGuid().ToString(),
                        Status = DiagnosticReport.DiagnosticReportStatus.Registered,
                        Code = new CodeableConcept()
                        {
                            Coding = new List<Coding>()
                            {
                                new Coding()
                                {
                                    Code = "12345-1",
                                    System = "https://loinc.org",
                                },
                                new Coding()
                                {
                                    Code = "12345-2",
                                    System = "https://loinc.org",
                                },
                                new Coding()
                                {
                                    Code = "12345-3",
                                    System = "https://loinc.org",
                                },
                            },
                        },
                        Subject = new ResourceReference("patient"),
                        PresentedForm = new List<Attachment>
                        {
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2005-12-24",
                                Url = "http://example.org/fhir/source/attachment",
                            },
                        },
                    },
                    new List<Resource>(),
                    new List<Resource>(),
                    0,
                    0,
                },
                new object[]
                {
                    // Update a new DiagnosticReport resource with attachments already existing.
                    new DiagnosticReport
                    {
                        Id = Guid.NewGuid().ToString(),
                        Status = DiagnosticReport.DiagnosticReportStatus.Registered,
                        Code = new CodeableConcept()
                        {
                            Coding = new List<Coding>()
                            {
                                new Coding()
                                {
                                    Code = "11488-4",
                                    System = "https://loinc.org",
                                },
                                new Coding()
                                {
                                    Code = "12345-3",
                                    System = "https://loinc.org",
                                },
                                new Coding()
                                {
                                    Code = "34117-2",
                                    System = "https://loinc.org",
                                },
                            },
                        },
                        Subject = new ResourceReference("patient"),
                        PresentedForm = new List<Attachment>
                        {
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2005-12-24",
                                Url = "http://example.org/fhir/source/attachment",
                            },
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2006-12-24",
                                Url = "http://example.org/fhir/source/attachment1",
                            },
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2007-12-24",
                                Url = "http://example.org/fhir/source/attachment2",
                            },
                        },
                    },
                    new List<Resource>(),
                    new List<Resource>
                    {
                        new DocumentReference
                        {
                            Id = Guid.NewGuid().ToString(),
                            Content = new List<DocumentReference.ContentComponent>
                            {
#if R4 || R4B || Stu3
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2005-12-24",
                                        Url = "http://example.org/fhir/source/attachment",
                                    },
                                    Format = new Coding()
                                    {
                                        Code = "11488-4",
                                        System = "https://loinc.org",
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2006-12-24",
                                        Url = "http://example.org/fhir/source/attachment1",
                                    },
                                    Format = new Coding()
                                    {
                                        Code = "11488-4",
                                        System = "https://loinc.org",
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2007-12-24",
                                        Url = "http://example.org/fhir/source/attachment2",
                                    },
                                    Format = new Coding()
                                    {
                                        Code = "11488-4",
                                        System = "https://loinc.org",
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2005-12-24",
                                        Url = "http://example.org/fhir/source/attachment",
                                    },
                                    Format = new Coding()
                                    {
                                        Code = "34117-2",
                                        System = "https://loinc.org",
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2006-12-24",
                                        Url = "http://example.org/fhir/source/attachment1",
                                    },
                                    Format = new Coding()
                                    {
                                        Code = "34117-2",
                                        System = "https://loinc.org",
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2007-12-24",
                                        Url = "http://example.org/fhir/source/attachment2",
                                    },
                                    Format = new Coding()
                                    {
                                        Code = "34117-2",
                                        System = "https://loinc.org",
                                    },
                                },
#else
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2005-12-24",
                                        Url = "http://example.org/fhir/source/attachment",
                                    },
                                    Profile = new List<DocumentReference.ProfileComponent>
                                    {
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "11488-4",
                                                System = "https://loinc.org",
                                            },
                                        },
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "34117-2",
                                                System = "https://loinc.org",
                                            },
                                        },
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2006-12-24",
                                        Url = "http://example.org/fhir/source/attachment1",
                                    },
                                    Profile = new List<DocumentReference.ProfileComponent>
                                    {
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "11488-4",
                                                System = "https://loinc.org",
                                            },
                                        },
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "34117-2",
                                                System = "https://loinc.org",
                                            },
                                        },
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2007-12-24",
                                        Url = "http://example.org/fhir/source/attachment2",
                                    },
                                    Profile = new List<DocumentReference.ProfileComponent>
                                    {
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "11488-4",
                                                System = "https://loinc.org",
                                            },
                                        },
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "34117-2",
                                                System = "https://loinc.org",
                                            },
                                        },
                                    },
                                },
#endif
                            },
                            Subject = new ResourceReference("patient"),
#if R4 || R4B || Stu3
                            Status = DocumentReferenceStatus.Current,
#else
                            Status = DocumentReference.DocumentReferenceStatus.Current,
#endif
                        },
                    },
                    1,
                    0,
                },
                new object[]
                {
                    // Update a new DiagnosticReport resource with multiple duplicate resources found.
                    new DiagnosticReport
                    {
                        Id = Guid.NewGuid().ToString(),
                        Status = DiagnosticReport.DiagnosticReportStatus.Registered,
                        Code = new CodeableConcept()
                        {
                            Coding = new List<Coding>()
                            {
                                new Coding()
                                {
                                    Code = "11488-4",
                                    System = "https://loinc.org",
                                },
                                new Coding()
                                {
                                    Code = "12345-3",
                                    System = "https://loinc.org",
                                },
                                new Coding()
                                {
                                    Code = "34117-2",
                                    System = "https://loinc.org",
                                },
                            },
                        },
                        Subject = new ResourceReference("patient"),
                        PresentedForm = new List<Attachment>
                        {
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2005-12-24",
                                Url = "http://example.org/fhir/source/attachment",
                            },
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2006-12-24",
                                Url = "http://example.org/fhir/source/attachment1",
                            },
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2007-12-24",
                                Url = "http://example.org/fhir/source/attachment2",
                            },
                        },
                    },
                    new List<Resource>
                    {
                        new DocumentReference
                        {
                            Id = "duplicate",
                            Content = new List<DocumentReference.ContentComponent>
                            {
#if R4 || R4B || Stu3
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2005-12-24",
                                        Url = "http://example.org/fhir/source/attachment",
                                    },
                                    Format = new Coding()
                                    {
                                        Code = "11488-4",
                                        System = "https://loinc.org",
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2006-12-24",
                                        Url = "http://example.org/fhir/source/attachment1",
                                    },
                                    Format = new Coding()
                                    {
                                        Code = "11488-4",
                                        System = "https://loinc.org",
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2007-12-24",
                                        Url = "http://example.org/fhir/source/attachment2",
                                    },
                                    Format = new Coding()
                                    {
                                        Code = "11488-4",
                                        System = "https://loinc.org",
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2008-12-24",
                                        Url = "http://example.org/fhir/duplicate/attachment",
                                    },
                                    Format = new Coding()
                                    {
                                        Code = "54321-0",
                                        System = "https://loinc.org",
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2005-12-24",
                                        Url = "http://example.org/fhir/source/attachment",
                                    },
                                    Format = new Coding()
                                    {
                                        Code = "34117-2",
                                        System = "https://loinc.org",
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2006-12-24",
                                        Url = "http://example.org/fhir/source/attachment1",
                                    },
                                    Format = new Coding()
                                    {
                                        Code = "34117-2",
                                        System = "https://loinc.org",
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2007-12-24",
                                        Url = "http://example.org/fhir/source/attachment2",
                                    },
                                    Format = new Coding()
                                    {
                                        Code = "34117-2",
                                        System = "https://loinc.org",
                                    },
                                },
#else
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2008-12-24",
                                        Url = "http://example.org/fhir/dupliate/attachment",
                                    },
                                    Profile = new List<DocumentReference.ProfileComponent>
                                    {
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "54321-0",
                                                System = "https://loinc.org",
                                            },
                                        },
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2005-12-24",
                                        Url = "http://example.org/fhir/source/attachment",
                                    },
                                    Profile = new List<DocumentReference.ProfileComponent>
                                    {
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "11488-4",
                                                System = "https://loinc.org",
                                            },
                                        },
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "34117-2",
                                                System = "https://loinc.org",
                                            },
                                        },
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2006-12-24",
                                        Url = "http://example.org/fhir/source/attachment1",
                                    },
                                    Profile = new List<DocumentReference.ProfileComponent>
                                    {
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "11488-4",
                                                System = "https://loinc.org",
                                            },
                                        },
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "34117-2",
                                                System = "https://loinc.org",
                                            },
                                        },
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2007-12-24",
                                        Url = "http://example.org/fhir/source/attachment2",
                                    },
                                    Profile = new List<DocumentReference.ProfileComponent>
                                    {
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "11488-4",
                                                System = "https://loinc.org",
                                            },
                                        },
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "34117-2",
                                                System = "https://loinc.org",
                                            },
                                        },
                                    },
                                },
#endif
                            },
                            Subject = new ResourceReference("patient"),
#if R4 || R4B || Stu3
                            Status = DocumentReferenceStatus.Current,
#else
                            Status = DocumentReference.DocumentReferenceStatus.Current,
#endif
                        },
                        new DocumentReference
                        {
                            Id = "duplicate1",
                            Content = new List<DocumentReference.ContentComponent>
                            {
#if R4 || R4B || Stu3
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2005-12-24",
                                        Url = "http://example.org/fhir/source/attachment",
                                    },
                                    Format = new Coding()
                                    {
                                        Code = "11488-4",
                                        System = "https://loinc.org",
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2006-12-24",
                                        Url = "http://example.org/fhir/source/attachment1",
                                    },
                                    Format = new Coding()
                                    {
                                        Code = "11488-4",
                                        System = "https://loinc.org",
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2007-12-24",
                                        Url = "http://example.org/fhir/source/attachment2",
                                    },
                                    Format = new Coding()
                                    {
                                        Code = "11488-4",
                                        System = "https://loinc.org",
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2008-12-24",
                                        Url = "http://example.org/fhir/duplicate1/attachment",
                                    },
                                    Format = new Coding()
                                    {
                                        Code = "54321-1",
                                        System = "https://loinc.org",
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2005-12-24",
                                        Url = "http://example.org/fhir/source/attachment",
                                    },
                                    Format = new Coding()
                                    {
                                        Code = "34117-2",
                                        System = "https://loinc.org",
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2006-12-24",
                                        Url = "http://example.org/fhir/source/attachment1",
                                    },
                                    Format = new Coding()
                                    {
                                        Code = "34117-2",
                                        System = "https://loinc.org",
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2007-12-24",
                                        Url = "http://example.org/fhir/source/attachment2",
                                    },
                                    Format = new Coding()
                                    {
                                        Code = "34117-2",
                                        System = "https://loinc.org",
                                    },
                                },
#else
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2008-12-24",
                                        Url = "http://example.org/fhir/dupliate1/attachment",
                                    },
                                    Profile = new List<DocumentReference.ProfileComponent>
                                    {
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "54321-1",
                                                System = "https://loinc.org",
                                            },
                                        },
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2005-12-24",
                                        Url = "http://example.org/fhir/source/attachment",
                                    },
                                    Profile = new List<DocumentReference.ProfileComponent>
                                    {
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "11488-4",
                                                System = "https://loinc.org",
                                            },
                                        },
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "34117-2",
                                                System = "https://loinc.org",
                                            },
                                        },
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2006-12-24",
                                        Url = "http://example.org/fhir/source/attachment1",
                                    },
                                    Profile = new List<DocumentReference.ProfileComponent>
                                    {
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "11488-4",
                                                System = "https://loinc.org",
                                            },
                                        },
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "34117-2",
                                                System = "https://loinc.org",
                                            },
                                        },
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2007-12-24",
                                        Url = "http://example.org/fhir/source/attachment2",
                                    },
                                    Profile = new List<DocumentReference.ProfileComponent>
                                    {
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "11488-4",
                                                System = "https://loinc.org",
                                            },
                                        },
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "34117-2",
                                                System = "https://loinc.org",
                                            },
                                        },
                                    },
                                },
#endif
                            },
                            Subject = new ResourceReference("patient"),
#if R4 || R4B || Stu3
                            Status = DocumentReferenceStatus.Current,
#else
                            Status = DocumentReference.DocumentReferenceStatus.Current,
#endif
                        },
                    },
                    new List<Resource>
                    {
                        new DocumentReference
                        {
                            Id = "duplicate",
                            Content = new List<DocumentReference.ContentComponent>
                            {
#if R4 || R4B || Stu3
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2008-12-24",
                                        Url = "http://example.org/fhir/duplicate/attachment",
                                    },
                                    Format = new Coding()
                                    {
                                        Code = "54321-0",
                                        System = "https://loinc.org",
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2005-12-24",
                                        Url = "http://example.org/fhir/source/attachment",
                                    },
                                    Format = new Coding()
                                    {
                                        Code = "34117-2",
                                        System = "https://loinc.org",
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2006-12-24",
                                        Url = "http://example.org/fhir/source/attachment1",
                                    },
                                    Format = new Coding()
                                    {
                                        Code = "34117-2",
                                        System = "https://loinc.org",
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2007-12-24",
                                        Url = "http://example.org/fhir/source/attachment2",
                                    },
                                    Format = new Coding()
                                    {
                                        Code = "34117-2",
                                        System = "https://loinc.org",
                                    },
                                },
#else
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2008-12-24",
                                        Url = "http://example.org/fhir/dupliate/attachment",
                                    },
                                    Profile = new List<DocumentReference.ProfileComponent>
                                    {
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "54321-0",
                                                System = "https://loinc.org",
                                            },
                                        },
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2005-12-24",
                                        Url = "http://example.org/fhir/source/attachment",
                                    },
                                    Profile = new List<DocumentReference.ProfileComponent>
                                    {
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "34117-2",
                                                System = "https://loinc.org",
                                            },
                                        },
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2006-12-24",
                                        Url = "http://example.org/fhir/source/attachment1",
                                    },
                                    Profile = new List<DocumentReference.ProfileComponent>
                                    {
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "34117-2",
                                                System = "https://loinc.org",
                                            },
                                        },
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2007-12-24",
                                        Url = "http://example.org/fhir/source/attachment2",
                                    },
                                    Profile = new List<DocumentReference.ProfileComponent>
                                    {
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "34117-2",
                                                System = "https://loinc.org",
                                            },
                                        },
                                    },
                                },
#endif
                            },
                            Subject = new ResourceReference("patient"),
#if R4 || R4B || Stu3
                            Status = DocumentReferenceStatus.Current,
#else
                            Status = DocumentReference.DocumentReferenceStatus.Current,
#endif
                        },
                        new DocumentReference
                        {
                            Id = "duplicate1",
                            Content = new List<DocumentReference.ContentComponent>
                            {
#if R4 || R4B || Stu3
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2005-12-24",
                                        Url = "http://example.org/fhir/source/attachment",
                                    },
                                    Format = new Coding()
                                    {
                                        Code = "11488-4",
                                        System = "https://loinc.org",
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2008-12-24",
                                        Url = "http://example.org/fhir/duplicate1/attachment",
                                    },
                                    Format = new Coding()
                                    {
                                        Code = "54321-1",
                                        System = "https://loinc.org",
                                    },
                                },
#else
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2005-12-24",
                                        Url = "http://example.org/fhir/source/attachment",
                                    },
                                    Profile = new List<DocumentReference.ProfileComponent>
                                    {
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "11488-4",
                                                System = "https://loinc.org",
                                            },
                                        },
                                    },
                                },
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2008-12-24",
                                        Url = "http://example.org/fhir/dupliate1/attachment",
                                    },
                                    Profile = new List<DocumentReference.ProfileComponent>
                                    {
                                        new DocumentReference.ProfileComponent()
                                        {
                                            Value = new Coding()
                                            {
                                                Code = "54321-1",
                                                System = "https://loinc.org",
                                            },
                                        },
                                    },
                                },
#endif
                            },
                            Subject = new ResourceReference("patient"),
#if R4 || R4B || Stu3
                            Status = DocumentReferenceStatus.Current,
#else
                            Status = DocumentReference.DocumentReferenceStatus.Current,
#endif
                        },
                    },
                    1,
                    2,
                },
                new object[]
                {
                    // Update a new DocumentReference resource with one attachment.
                    new DocumentReference
                    {
                        Id = Guid.NewGuid().ToString(),
                        Content = new List<DocumentReference.ContentComponent>
                        {
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2005-12-24",
                                    Url = "http://example.org/fhir/source/attachment",
                                },
#if R4 || R4B || Stu3
                                Format = new Coding()
                                {
                                    Code = "11488-4",
                                    System = "https://loinc.org",
                                },
#else
                                Profile = new List<DocumentReference.ProfileComponent>
                                {
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding()
                                        {
                                            Code = "11488-4",
                                            System = "https://loinc.org",
                                        },
                                    },
                                },
#endif
                            },
                        },
                        Subject = new ResourceReference("patient"),
#if R4 || R4B || Stu3
                        Status = DocumentReferenceStatus.Current,
#else
                        Status = DocumentReference.DocumentReferenceStatus.Current,
#endif
                    },
                    new List<Resource>
                    {
                        new DiagnosticReport
                        {
                            Id = "duplicate",
                            Status = DiagnosticReport.DiagnosticReportStatus.Registered,
                            Code = new CodeableConcept()
                            {
                                Coding = new List<Coding>()
                                {
                                    new Coding()
                                    {
                                        Code = "11488-4",
                                        System = "https://loinc.org",
                                    },
                                },
                            },
                            Subject = new ResourceReference("patient"),
                            PresentedForm = new List<Attachment>
                            {
                                new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2005-12-24",
                                    Url = "http://example.org/fhir/duplicate/attachment",
                                },
                                new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2005-12-24",
                                    Url = "http://example.org/fhir/source/attachment",
                                },
                            },
                        },
                    },
                    new List<Resource>
                    {
                        new DiagnosticReport
                        {
                            Id = "duplicate",
                            Status = DiagnosticReport.DiagnosticReportStatus.Registered,
                            Code = new CodeableConcept()
                            {
                                Coding = new List<Coding>()
                                {
                                    new Coding()
                                    {
                                        Code = "11488-4",
                                        System = "https://loinc.org",
                                    },
                                },
                            },
                            Subject = new ResourceReference("patient"),
                            PresentedForm = new List<Attachment>
                            {
                                new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2005-12-24",
                                    Url = "http://example.org/fhir/duplicate/attachment",
                                },
                            },
                        },
                    },
                    1,
                    1,
                },
                new object[]
                {
                    // Update a new DocumentReference resource with multiple codes and attachments.
                    new DocumentReference
                    {
                        Id = Guid.NewGuid().ToString(),
                        Content = new List<DocumentReference.ContentComponent>
                        {
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2005-12-24",
                                    Url = "http://example.org/fhir/source/attachment",
                                },
#if R4 || R4B || Stu3
                                Format = new Coding()
                                {
                                    Code = "11488-4",
                                    System = "https://loinc.org",
                                },
#else
                                Profile = new List<DocumentReference.ProfileComponent>
                                {
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding()
                                        {
                                            Code = "11488-4",
                                            System = "https://loinc.org",
                                        },
                                    },
                                },
#endif
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2006-12-24",
                                    Url = "http://example.org/fhir/source/attachment1",
                                },
#if R4 || R4B || Stu3
                                Format = new Coding()
                                {
                                    Code = "12345-1",
                                    System = "https://loinc.org",
                                },
#else
                                Profile = new List<DocumentReference.ProfileComponent>
                                {
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding()
                                        {
                                            Code = "12345-1",
                                            System = "https://loinc.org",
                                        },
                                    },
                                },
#endif
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2007-12-24",
                                    Url = "http://example.org/fhir/source/attachment2",
                                },
#if R4 || R4B || Stu3
                                Format = new Coding()
                                {
                                    Code = "18748-4",
                                    System = "https://loinc.org",
                                },
#else
                                Profile = new List<DocumentReference.ProfileComponent>
                                {
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding()
                                        {
                                            Code = "18748-4",
                                            System = "https://loinc.org",
                                        },
                                    },
                                },
#endif
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2005-12-24",
                                    Url = "http://example.org/fhir/source/attachment3",
                                },
#if R4 || R4B || Stu3
                                Format = new Coding()
                                {
                                    Code = "12345-2",
                                    System = "https://loinc.org",
                                },
#else
                                Profile = new List<DocumentReference.ProfileComponent>
                                {
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding()
                                        {
                                            Code = "12345-2",
                                            System = "https://loinc.org",
                                        },
                                    },
                                },
#endif
                            },
                        },
                        Subject = new ResourceReference(Guid.NewGuid().ToString()),
#if R4 || R4B || Stu3
                        Status = DocumentReferenceStatus.Current,
#else
                        Status = DocumentReference.DocumentReferenceStatus.Current,
#endif
                    },
                    new List<Resource>
                    {
                        new DiagnosticReport
                        {
                            Id = "duplicate",
                            Status = DiagnosticReport.DiagnosticReportStatus.Registered,
                            Code = new CodeableConcept()
                            {
                                Coding = new List<Coding>()
                                {
                                    new Coding()
                                    {
                                        Code = "11488-4",
                                        System = "https://loinc.org",
                                    },
                                    new Coding()
                                    {
                                        Code = "12345-3",
                                        System = "https://loinc.org",
                                    },
                                    new Coding()
                                    {
                                        Code = "18748-4",
                                        System = "https://loinc.org",
                                    },
                                },
                            },
                            Subject = new ResourceReference("patient"),
                            PresentedForm = new List<Attachment>
                            {
                                new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2005-12-24",
                                    Url = "http://example.org/fhir/source/attachment",
                                },
                                new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2006-12-24",
                                    Url = "http://example.org/fhir/duplicate/attachment1",
                                },
                                new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2007-12-24",
                                    Url = "http://example.org/fhir/source/attachment2",
                                },
                            },
                        },
                    },
                    new List<Resource>
                    {
                        new DiagnosticReport
                        {
                            Id = "duplicate",
                            Status = DiagnosticReport.DiagnosticReportStatus.Registered,
                            Code = new CodeableConcept()
                            {
                                Coding = new List<Coding>()
                                {
                                    new Coding()
                                    {
                                        Code = "11488-4",
                                        System = "https://loinc.org",
                                    },
                                    new Coding()
                                    {
                                        Code = "12345-3",
                                        System = "https://loinc.org",
                                    },
                                    new Coding()
                                    {
                                        Code = "18748-4",
                                        System = "https://loinc.org",
                                    },
                                },
                            },
                            Subject = new ResourceReference("patient"),
                            PresentedForm = new List<Attachment>
                            {
                                new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2006-12-24",
                                    Url = "http://example.org/fhir/duplicate/attachment1",
                                },
                            },
                        },
                    },
                    1,
                    1,
                },
                new object[]
                {
                    // Update a new DocumentReference resource without any clinical reference code.
                    new DocumentReference
                    {
                        Id = Guid.NewGuid().ToString(),
                        Content = new List<DocumentReference.ContentComponent>
                        {
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2005-12-24",
                                    Url = "http://example.org/fhir/source/attachment",
                                },
#if R4 || R4B || Stu3
                                Format = new Coding()
                                {
                                    Code = "54321-0",
                                    System = "https://loinc.org",
                                },
#else
                                Profile = new List<DocumentReference.ProfileComponent>
                                {
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding()
                                        {
                                            Code = "54321-0",
                                            System = "https://loinc.org",
                                        },
                                    },
                                },
#endif
                            },
                        },
                        Subject = new ResourceReference("patient"),
#if R4 || R4B || Stu3
                        Status = DocumentReferenceStatus.Current,
#else
                        Status = DocumentReference.DocumentReferenceStatus.Current,
#endif
                    },
                    new List<Resource>(),
                    new List<Resource>(),
                    0,
                    0,
                },
                new object[]
                {
                    // Update a new DocumentReference resource with codes and attachments already existing.
                    new DocumentReference
                    {
                        Id = Guid.NewGuid().ToString(),
                        Content = new List<DocumentReference.ContentComponent>
                        {
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2005-12-24",
                                    Url = "http://example.org/fhir/source/attachment",
                                },
#if R4 || R4B || Stu3
                                Format = new Coding()
                                {
                                    Code = "11488-4",
                                    System = "https://loinc.org",
                                },
#else
                                Profile = new List<DocumentReference.ProfileComponent>
                                {
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding()
                                        {
                                            Code = "11488-4",
                                            System = "https://loinc.org",
                                        },
                                    },
                                },
#endif
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2006-12-24",
                                    Url = "http://example.org/fhir/source/attachment1",
                                },
#if R4 || R4B || Stu3
                                Format = new Coding()
                                {
                                    Code = "12345-1",
                                    System = "https://loinc.org",
                                },
#else
                                Profile = new List<DocumentReference.ProfileComponent>
                                {
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding()
                                        {
                                            Code = "12345-1",
                                            System = "https://loinc.org",
                                        },
                                    },
                                },
#endif
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2007-12-24",
                                    Url = "http://example.org/fhir/source/attachment2",
                                },
#if R4 || R4B || Stu3
                                Format = new Coding()
                                {
                                    Code = "18748-4",
                                    System = "https://loinc.org",
                                },
#else
                                Profile = new List<DocumentReference.ProfileComponent>
                                {
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding()
                                        {
                                            Code = "18748-4",
                                            System = "https://loinc.org",
                                        },
                                    },
                                },
#endif
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2005-12-24",
                                    Url = "http://example.org/fhir/source/attachment3",
                                },
#if R4 || R4B || Stu3
                                Format = new Coding()
                                {
                                    Code = "12345-2",
                                    System = "https://loinc.org",
                                },
#else
                                Profile = new List<DocumentReference.ProfileComponent>
                                {
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding()
                                        {
                                            Code = "12345-2",
                                            System = "https://loinc.org",
                                        },
                                    },
                                },
#endif
                            },
                        },
                        Subject = new ResourceReference(Guid.NewGuid().ToString()),
#if R4 || R4B || Stu3
                        Status = DocumentReferenceStatus.Current,
#else
                        Status = DocumentReference.DocumentReferenceStatus.Current,
#endif
                    },
                    new List<Resource>(),
                    new List<Resource>
                    {
                        new DiagnosticReport
                        {
                            Id = "duplicate",
                            Status = DiagnosticReport.DiagnosticReportStatus.Registered,
                            Code = new CodeableConcept()
                            {
                                Coding = new List<Coding>()
                                {
                                    new Coding()
                                    {
                                        Code = "11488-4",
                                        System = "https://loinc.org",
                                    },
                                    new Coding()
                                    {
                                        Code = "12345-3",
                                        System = "https://loinc.org",
                                    },
                                    new Coding()
                                    {
                                        Code = "18748-4",
                                        System = "https://loinc.org",
                                    },
                                },
                            },
                            Subject = new ResourceReference("patient"),
                            PresentedForm = new List<Attachment>
                            {
                                new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2005-12-24",
                                    Url = "http://example.org/fhir/source/attachment",
                                },
                                new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2006-12-24",
                                    Url = "http://example.org/fhir/duplicate/attachment1",
                                },
                                new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2007-12-24",
                                    Url = "http://example.org/fhir/source/attachment2",
                                },
                            },
                        },
                    },
                    1,
                    0,
                },
                new object[]
                {
                    // Update a new DocumentReference resource with multiple duplicate resources found.
                    new DocumentReference
                    {
                        Id = Guid.NewGuid().ToString(),
                        Content = new List<DocumentReference.ContentComponent>
                        {
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2005-12-24",
                                    Url = "http://example.org/fhir/source/attachment",
                                },
#if R4 || R4B || Stu3
                                Format = new Coding()
                                {
                                    Code = "11488-4",
                                    System = "https://loinc.org",
                                },
#else
                                Profile = new List<DocumentReference.ProfileComponent>
                                {
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding()
                                        {
                                            Code = "11488-4",
                                            System = "https://loinc.org",
                                        },
                                    },
                                },
#endif
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2006-12-24",
                                    Url = "http://example.org/fhir/source/attachment1",
                                },
#if R4 || R4B || Stu3
                                Format = new Coding()
                                {
                                    Code = "12345-1",
                                    System = "https://loinc.org",
                                },
#else
                                Profile = new List<DocumentReference.ProfileComponent>
                                {
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding()
                                        {
                                            Code = "12345-1",
                                            System = "https://loinc.org",
                                        },
                                    },
                                },
#endif
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2007-12-24",
                                    Url = "http://example.org/fhir/source/attachment2",
                                },
#if R4 || R4B || Stu3
                                Format = new Coding()
                                {
                                    Code = "18748-4",
                                    System = "https://loinc.org",
                                },
#else
                                Profile = new List<DocumentReference.ProfileComponent>
                                {
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding()
                                        {
                                            Code = "18748-4",
                                            System = "https://loinc.org",
                                        },
                                    },
                                },
#endif
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2005-12-24",
                                    Url = "http://example.org/fhir/source/attachment3",
                                },
#if R4 || R4B || Stu3
                                Format = new Coding()
                                {
                                    Code = "12345-2",
                                    System = "https://loinc.org",
                                },
#else
                                Profile = new List<DocumentReference.ProfileComponent>
                                {
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding()
                                        {
                                            Code = "12345-2",
                                            System = "https://loinc.org",
                                        },
                                    },
                                },
#endif
                            },
                        },
                        Subject = new ResourceReference(Guid.NewGuid().ToString()),
#if R4 || R4B || Stu3
                        Status = DocumentReferenceStatus.Current,
#else
                        Status = DocumentReference.DocumentReferenceStatus.Current,
#endif
                    },
                    new List<Resource>
                    {
                        new DiagnosticReport
                        {
                            Id = "duplicate",
                            Status = DiagnosticReport.DiagnosticReportStatus.Registered,
                            Code = new CodeableConcept()
                            {
                                Coding = new List<Coding>()
                                {
                                    new Coding()
                                    {
                                        Code = "12345-3",
                                        System = "https://loinc.org",
                                    },
                                    new Coding()
                                    {
                                        Code = "18748-4",
                                        System = "https://loinc.org",
                                    },
                                },
                            },
                            Subject = new ResourceReference("patient"),
                            PresentedForm = new List<Attachment>
                            {
                                new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2007-12-24",
                                    Url = "http://example.org/fhir/source/attachment2",
                                },
                            },
                        },
                        new DiagnosticReport
                        {
                            Id = "duplicate1",
                            Status = DiagnosticReport.DiagnosticReportStatus.Registered,
                            Code = new CodeableConcept()
                            {
                                Coding = new List<Coding>()
                                {
                                    new Coding()
                                    {
                                        Code = "11488-4",
                                        System = "https://loinc.org",
                                    },
                                    new Coding()
                                    {
                                        Code = "12345-3",
                                        System = "https://loinc.org",
                                    },
                                    new Coding()
                                    {
                                        Code = "18748-4",
                                        System = "https://loinc.org",
                                    },
                                },
                            },
                            Subject = new ResourceReference("patient"),
                            PresentedForm = new List<Attachment>
                            {
                                new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2005-12-24",
                                    Url = "http://example.org/fhir/source/attachment",
                                },
                                new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2006-12-24",
                                    Url = "http://example.org/fhir/duplicate/attachment1",
                                },
                                new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2007-12-24",
                                    Url = "http://example.org/fhir/source/attachment2",
                                },
                            },
                        },
                    },
                    new List<Resource>
                    {
                        new DiagnosticReport
                        {
                            Id = "duplicate",
                            Status = DiagnosticReport.DiagnosticReportStatus.Registered,
                            Code = new CodeableConcept()
                            {
                                Coding = new List<Coding>()
                                {
                                    new Coding()
                                    {
                                        Code = "18748-4",
                                        System = "https://loinc.org",
                                    },
                                    new Coding()
                                    {
                                        Code = "12345-3",
                                        System = "https://loinc.org",
                                    },
                                },
                            },
                            Subject = new ResourceReference("patient"),
                            PresentedForm = new List<Attachment>(),
                        },
                        new DiagnosticReport
                        {
                            Id = "duplicate1",
                            Status = DiagnosticReport.DiagnosticReportStatus.Registered,
                            Code = new CodeableConcept()
                            {
                                Coding = new List<Coding>()
                                {
                                    new Coding()
                                    {
                                        Code = "11488-4",
                                        System = "https://loinc.org",
                                    },
                                    new Coding()
                                    {
                                        Code = "12345-3",
                                        System = "https://loinc.org",
                                    },
                                    new Coding()
                                    {
                                        Code = "18748-4",
                                        System = "https://loinc.org",
                                    },
                                },
                            },
                            Subject = new ResourceReference("patient"),
                            PresentedForm = new List<Attachment>
                            {
                                new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2006-12-24",
                                    Url = "http://example.org/fhir/duplicate/attachment1",
                                },
                            },
                        },
                    },
                    1,
                    2,
                },
            };

            foreach (var d in data)
            {
                yield return d;
            }
        }

        public static IEnumerable<object[]> GetDeleteResourceData()
        {
            var data = new[]
            {
                new object[]
                {
                    // Delete a DiagnosticReport resource with one duplicate resource.
                    new DiagnosticReport
                    {
                        Id = "source",
                        Status = DiagnosticReport.DiagnosticReportStatus.Registered,
                        Code = new CodeableConcept()
                        {
                            Coding = new List<Coding>()
                            {
                                new Coding()
                                {
                                    Code = "12345",
                                },
                            },
                        },
                        Subject = new ResourceReference(Guid.NewGuid().ToString()),
                        PresentedForm = new List<Attachment>
                        {
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2005-12-24",
                                Url = "http://example.org/fhir/Binary/attachment-source",
                            },
                        },
                    },
                    new List<Resource>
                    {
                        new DocumentReference
                        {
                            Id = "duplicate",
                            Meta = new Meta()
                            {
                                Tag = new List<Coding>
                                {
                                    new Coding(ClinicalReferenceDuplicator.TagDuplicateCreatedOn, DateTime.UtcNow.ToString("o")),
                                    new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "source"),
                                },
                            },
                            Content = new List<DocumentReference.ContentComponent>
                            {
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2005-12-24",
                                        Url = "http://example.org/fhir/Binary/attachment-source",
                                    },
                                },
                            },
                            Subject = new ResourceReference(Guid.NewGuid().ToString()),
#if R4 || R4B || Stu3
                            Status = DocumentReferenceStatus.Current,
#else
                            Status = DocumentReference.DocumentReferenceStatus.Current,
#endif
                        },
                    },
                    DeleteOperation.SoftDelete,
                    1,
                    1,
                },
                new object[]
                {
                    // Delete a  DiagnosticReport resource with multiple duplicate resources.
                    new DiagnosticReport
                    {
                        Id = "source",
                        Status = DiagnosticReport.DiagnosticReportStatus.Registered,
                        Code = new CodeableConcept()
                        {
                            Coding = new List<Coding>()
                            {
                                new Coding()
                                {
                                    Code = "12345",
                                },
                            },
                        },
                        Subject = new ResourceReference(Guid.NewGuid().ToString()),
                        PresentedForm = new List<Attachment>
                        {
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2005-12-24",
                                Url = "http://example.org/fhir/Binary/attachment-source",
                            },
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2006-12-24",
                                Url = "http://example.org/fhir/Binary/attachment-source1",
                            },
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2006-12-24",
                                Url = "http://example.org/fhir/Binary/attachment-source2",
                            },
                        },
                    },
                    new List<Resource>
                    {
                        new DocumentReference
                        {
                            Id = "duplicate",
                            Meta = new Meta()
                            {
                                Tag = new List<Coding>
                                {
                                    new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "source"),
                                },
                            },
                            Content = new List<DocumentReference.ContentComponent>
                            {
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2005-12-24",
                                        Url = "http://example.org/fhir/Binary/attachment-duplicate",
                                    },
                                },
                            },
                            Subject = new ResourceReference(Guid.NewGuid().ToString()),
#if R4 || R4B || Stu3
                            Status = DocumentReferenceStatus.Current,
#else
                            Status = DocumentReference.DocumentReferenceStatus.Current,
#endif
                        },
                        new DocumentReference
                        {
                            Id = "duplicate1",
                            Meta = new Meta()
                            {
                                Tag = new List<Coding>
                                {
                                    new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "source"),
                                },
                            },
                            Content = new List<DocumentReference.ContentComponent>
                            {
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2005-12-24",
                                        Url = "http://example.org/fhir/Binary/attachment-duplicate1",
                                    },
                                },
                            },
                            Subject = new ResourceReference(Guid.NewGuid().ToString()),
#if R4 || R4B || Stu3
                            Status = DocumentReferenceStatus.Current,
#else
                            Status = DocumentReference.DocumentReferenceStatus.Current,
#endif
                        },
                        new DocumentReference
                        {
                            Id = "duplicate2",
                            Meta = new Meta()
                            {
                                Tag = new List<Coding>
                                {
                                    new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "source"),
                                },
                            },
                            Content = new List<DocumentReference.ContentComponent>
                            {
                                new DocumentReference.ContentComponent()
                                {
                                    Attachment = new Attachment()
                                    {
                                        ContentType = "application/xhtml",
                                        Creation = "2005-12-24",
                                        Url = "http://example.org/fhir/Binary/attachment-duplicate2",
                                    },
                                },
                            },
                            Subject = new ResourceReference(Guid.NewGuid().ToString()),
#if R4 || R4B || Stu3
                            Status = DocumentReferenceStatus.Current,
#else
                            Status = DocumentReference.DocumentReferenceStatus.Current,
#endif
                        },
                    },
                    DeleteOperation.HardDelete,
                    1,
                    3,
                },
                new object[]
                {
                    // Delete a DiagnosticReport resource without duplicate resource.
                    new DiagnosticReport
                    {
                        Id = "source",
                        Status = DiagnosticReport.DiagnosticReportStatus.Registered,
                        Code = new CodeableConcept()
                        {
                            Coding = new List<Coding>()
                            {
                                new Coding()
                                {
                                    Code = "12345",
                                },
                            },
                        },
                        Subject = new ResourceReference(Guid.NewGuid().ToString()),
                        PresentedForm = new List<Attachment>
                        {
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2005-12-24",
                                Url = "http://example.org/fhir/Binary/attachment-source",
                            },
                        },
                    },
                    new List<Resource>(),
                    DeleteOperation.SoftDelete,
                    1,
                    0,
                },
            };

            foreach (var d in data)
            {
                yield return d;
            }
        }
    }
}
