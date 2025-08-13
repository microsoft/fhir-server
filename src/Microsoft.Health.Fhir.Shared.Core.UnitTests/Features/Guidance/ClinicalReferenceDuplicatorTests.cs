// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Guidance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
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
            Resource resource)
        {
            var resourceType = resource.TypeName;
            var duplicateResourceType = string.Equals(resourceType, KnownResourceTypes.DiagnosticReport)
                ? KnownResourceTypes.DocumentReference : KnownResourceTypes.DiagnosticReport;

            // Set up a validation on a request for creating the source resource.
            var resourceElement = resource.ToResourceElement();
            _dataStore.UpsertAsync(
                Arg.Is<ResourceWrapperOperation>(x => string.Equals(x.Wrapper.ResourceTypeName, resourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>())
                .Throws(new Exception($"Shouldn't be called to create a {resourceType} resource."));

            // Set up a validation on a request for creating a duplicate resource.
            Resource duplicateResource = null;
            _dataStore.UpsertAsync(
                Arg.Is<ResourceWrapperOperation>(x => string.Equals(x.Wrapper.ResourceTypeName, duplicateResourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>())
                .Returns(
                    x =>
                    {
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
                            x => string.Equals(x.System, ClinicalReferenceDuplicator.TagIsDuplicate, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(x.Code, bool.TrueString, StringComparison.OrdinalIgnoreCase));

                        if (string.Equals(duplicateResourceType, KnownResourceTypes.DiagnosticReport, StringComparison.OrdinalIgnoreCase))
                        {
                            var source = (DocumentReference)resource;
                            var duplicate = (DiagnosticReport)r;

                            Assert.Equal(source.Subject?.Reference, duplicate.Subject?.Reference);
                            Assert.NotNull(duplicate.PresentedForm);
                            foreach (var a in source.Content.Select(x => x.Attachment))
                            {
                                Assert.Contains(
                                    duplicate.PresentedForm,
                                    x => string.Equals(x.Url, a.Url, StringComparison.OrdinalIgnoreCase));
                            }

                            duplicateResource = duplicate;
                        }
                        else
                        {
                            var source = (DiagnosticReport)resource;
                            var duplicate = (DocumentReference)r;

                            Assert.Equal(source.Subject?.Reference, duplicate.Subject?.Reference);
                            Assert.NotNull(duplicate.Content);
                            foreach (var a in source.PresentedForm)
                            {
                                Assert.Contains(
                                    duplicate.Content,
                                    x => string.Equals(x.Attachment?.Url, a.Url, StringComparison.OrdinalIgnoreCase));
                            }

                            duplicateResource = duplicate;
                        }

                        if (string.IsNullOrEmpty(r.Id))
                        {
                            r.Id = Guid.NewGuid().ToString();
                        }

                        return Task.FromResult(
                            new UpsertOutcome(
                                CreateResourceWrapper(r.ToResourceElement()),
                                SaveOutcomeType.Created));
                    });

            // Set up a validation on a request for updating the source resource with the id of the duplicate resource.
            _dataStore.UpsertAsync(
                Arg.Is<ResourceWrapperOperation>(x => string.Equals(x.Wrapper.ResourceTypeName, resourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>())
                .Returns(
                    x =>
                    {
                        var re = ((ResourceWrapperOperation)x[0])?.Wrapper?.RawResource?
                            .ToITypedElement(ModelInfoProvider.Instance)?
                            .ToResourceElement();
                        Assert.NotNull(re);
                        Assert.Equal(resourceType, re.InstanceType, true);

                        var r = re.ToPoco();
                        Assert.NotNull(r.Meta?.Tag);
                        Assert.Contains(
                            r.Meta.Tag,
                            t => string.Equals(t.System, ClinicalReferenceDuplicator.TagDuplicateOf, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(t.Code, duplicateResource.Id, StringComparison.OrdinalIgnoreCase));

                        return Task.FromResult(
                            new UpsertOutcome(
                                CreateResourceWrapper(r.ToResourceElement()),
                                SaveOutcomeType.Updated));
                    });

            await _duplicator.CreateResourceAsync(
                new RawResourceElement(CreateResourceWrapper(resourceElement)),
                CancellationToken.None);

            // Check how many times create/update was invoked.
            await _dataStore.Received(1).UpsertAsync(
                Arg.Is<ResourceWrapperOperation>(x => string.Equals(x.Wrapper.ResourceTypeName, duplicateResourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>());
            await _dataStore.Received(1).UpsertAsync(
                Arg.Is<ResourceWrapperOperation>(x => string.Equals(x.Wrapper.ResourceTypeName, resourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [MemberData(nameof(GetUpdateResourceData))]
        public async Task GivenResource_WhenUpdating_ThenDuplicateResourceShouldBeUpdated(
            Resource resource,
            List<Resource> duplicateResources)
        {
            var resourceType = resource.TypeName;
            var duplicateResourceType = string.Equals(resourceType, KnownResourceTypes.DiagnosticReport)
                ? KnownResourceTypes.DocumentReference : KnownResourceTypes.DiagnosticReport;

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

            // Set up a validation on a request for creating/updating a duplicate resource.
            Resource duplicateResource = null;
            _dataStore.UpsertAsync(
                Arg.Is<ResourceWrapperOperation>(x => string.Equals(x.Wrapper.ResourceTypeName, duplicateResourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>())
                .Returns(
                    x =>
                    {
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
                            x => string.Equals(x.System, ClinicalReferenceDuplicator.TagIsDuplicate, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(x.Code, bool.TrueString, StringComparison.OrdinalIgnoreCase));

                        if (string.Equals(duplicateResourceType, KnownResourceTypes.DiagnosticReport, StringComparison.OrdinalIgnoreCase))
                        {
                            var source = (DocumentReference)resource;
                            var duplicate = (DiagnosticReport)r;

                            Assert.Equal(source.Subject?.Reference, duplicate.Subject?.Reference);
                            Assert.NotNull(duplicate.PresentedForm);
                            foreach (var a in source.Content.Select(x => x.Attachment))
                            {
                                Assert.Contains(
                                    duplicate.PresentedForm,
                                    x => string.Equals(x.Url, a.Url, StringComparison.OrdinalIgnoreCase));
                            }

                            duplicateResource = duplicate;
                        }
                        else
                        {
                            var source = (DiagnosticReport)resource;
                            var duplicate = (DocumentReference)r;

                            Assert.Equal(source.Subject?.Reference, duplicate.Subject?.Reference);
                            Assert.NotNull(duplicate.Content);
                            foreach (var a in source.PresentedForm)
                            {
                                Assert.Contains(
                                    duplicate.Content,
                                    x => string.Equals(x.Attachment?.Url, a.Url, StringComparison.OrdinalIgnoreCase));
                            }

                            duplicateResource = duplicate;
                        }

                        if (string.IsNullOrEmpty(r.Id))
                        {
                            r.Id = Guid.NewGuid().ToString();
                        }

                        return Task.FromResult(
                            new UpsertOutcome(
                                CreateResourceWrapper(r.ToResourceElement()),
                                SaveOutcomeType.Created));
                    });

            // Set up a validation on a request for updating the source resource with the id of the duplicate resource.
            _dataStore.UpsertAsync(
                Arg.Is<ResourceWrapperOperation>(x => string.Equals(x.Wrapper.ResourceTypeName, resourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>())
                .Returns(
                    x =>
                    {
                        var re = ((ResourceWrapperOperation)x[0])?.Wrapper?.RawResource?
                            .ToITypedElement(ModelInfoProvider.Instance)?
                            .ToResourceElement();
                        Assert.NotNull(re);
                        Assert.Equal(resourceType, re.InstanceType, true);

                        var r = re.ToPoco();
                        Assert.NotNull(r.Meta?.Tag);
                        Assert.Contains(
                            r.Meta.Tag,
                            t => string.Equals(t.System, ClinicalReferenceDuplicator.TagDuplicateOf, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(t.Code, duplicateResource.Id, StringComparison.OrdinalIgnoreCase));

                        return Task.FromResult(
                            new UpsertOutcome(
                                CreateResourceWrapper(r.ToResourceElement()),
                                SaveOutcomeType.Updated));
                    });

            await _duplicator.UpdateResourceAsync(
                new RawResourceElement(CreateResourceWrapper(resource.ToResourceElement())),
                CancellationToken.None);

            // Check how many times create/update was invoked.
            await _searchService.Received(1).SearchAsync(
                Arg.Is<string>(x => string.Equals(x, duplicateResourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>());
            await _dataStore.Received(duplicateResources.Any() ? 0 : 1).UpsertAsync(
                Arg.Is<ResourceWrapperOperation>(x => string.Equals(x.Wrapper.ResourceTypeName, resourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>());
            await _dataStore.Received(duplicateResources.Any() ? duplicateResources.Count : 1).UpsertAsync(
                Arg.Is<ResourceWrapperOperation>(x => string.Equals(x.Wrapper.ResourceTypeName, duplicateResourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [MemberData(nameof(GetDeleteResourceData))]
        public async Task GivenResource_WhenDeleting_ThenDuplicateResourceShouldBeDeleted(
            Resource resource,
            DeleteOperation deleteOperation,
            List<Resource> duplicateResources)
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
            await _searchService.Received(1).SearchAsync(
                Arg.Is<string>(x => string.Equals(x, duplicateResourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>());
            await _dataStore.Received(deleteOperation == DeleteOperation.SoftDelete && duplicateResources.Any() ? duplicateResources.Count : 0).UpsertAsync(
                Arg.Is<ResourceWrapperOperation>(x => string.Equals(x.Wrapper.ResourceTypeName, duplicateResourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>());
            await _dataStore.Received(deleteOperation == DeleteOperation.HardDelete && duplicateResources.Any() ? duplicateResources.Count : 0).HardDeleteAsync(
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
                                Url = "http://example.org/fhir/Binary/attachment",
                            },
                        },
                    },
                },
                new object[]
                {
                    // Create a new DiagnosticReport resource with multiple attachments.
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
                                    Code = "12345",
                                },
                            },
                        },
                        Subject = new ResourceReference(Guid.NewGuid().ToString()),
                    },
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
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2006-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment1",
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
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2005-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment3",
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
                new object[]
                {
                    // Create a new DocumentReference resource without any attachment.
                    new DocumentReference
                    {
                        Id = Guid.NewGuid().ToString(),
                        Subject = new ResourceReference(Guid.NewGuid().ToString()),
    #if R4 || R4B || Stu3
                        Status = DocumentReferenceStatus.Current,
    #else
                        Status = DocumentReference.DocumentReferenceStatus.Current,
    #endif
                    },
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
                    // Update a DiagnosticReport resource with one attachment.
                    new DiagnosticReport
                    {
                        Id = "source",
                        Meta = new Meta()
                        {
                            Tag = new List<Coding>
                            {
                                new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "duplicate"),
                            },
                        },
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
                                    new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "source"),
                                    new Coding(ClinicalReferenceDuplicator.TagIsDuplicate, "true"),
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
                    },
                },
                new object[]
                {
                    // Update a  DiagnosticReport resource with multiple attachments.
                    new DiagnosticReport
                    {
                        Id = "source",
                        Meta = new Meta()
                        {
                            Tag = new List<Coding>
                            {
                                new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "duplicate"),
                            },
                        },
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
                                    new Coding(ClinicalReferenceDuplicator.TagIsDuplicate, "true"),
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
                    },
                },
                new object[]
                {
                    // Update a DiagnosticReport resource without any attachment.
                    new DiagnosticReport
                    {
                        Id = "source",
                        Meta = new Meta()
                        {
                            Tag = new List<Coding>
                            {
                                new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "duplicate"),
                            },
                        },
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
                        PresentedForm = new List<Attachment>(),
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
                                    new Coding(ClinicalReferenceDuplicator.TagIsDuplicate, "true"),
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
                    },
                },
                new object[]
                {
                    // Update a DiagnosticReport resource with attachments when a duplicate resource doesn't exist.
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
                                Creation = "2007-12-24",
                                Url = "http://example.org/fhir/Binary/attachment-source2",
                            },
                        },
                    },
                    new List<Resource>(),
                },
                new object[]
                {
                    // Update a DiagnosticReport resource with multiple duplicates.
                    new DiagnosticReport
                    {
                        Id = "source",
                        Meta = new Meta()
                        {
                            Tag = new List<Coding>
                            {
                                new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "duplicate"),
                            },
                        },
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
                                    new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "source"),
                                    new Coding(ClinicalReferenceDuplicator.TagIsDuplicate, "true"),
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
                                    new Coding(ClinicalReferenceDuplicator.TagIsDuplicate, "true"),
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
                                    new Coding(ClinicalReferenceDuplicator.TagIsDuplicate, "true"),
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
                },
                new object[]
                {
                    // Update a DocumentReference resource with one attachment.
                    new DocumentReference
                    {
                        Id = "source",
                        Meta = new Meta()
                        {
                            Tag = new List<Coding>
                            {
                                new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "duplicate"),
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
                    new List<Resource>
                    {
                        new DiagnosticReport
                        {
                            Id = "duplicate",
                            Meta = new Meta()
                            {
                                Tag = new List<Coding>
                                {
                                    new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "source"),
                                    new Coding(ClinicalReferenceDuplicator.TagIsDuplicate, "true"),
                                },
                            },
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
                                    Url = "http://example.org/fhir/Binary/attachment-duplicate",
                                },
                            },
                        },
                    },
                },
                new object[]
                {
                    // Update a DocumentReference resource with multiple attachment.
                    new DocumentReference
                    {
                        Id = "source",
                        Meta = new Meta()
                        {
                            Tag = new List<Coding>
                            {
                                new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "duplicate"),
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
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2006-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment-source1",
                                },
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2007-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment-source2",
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
                    new List<Resource>
                    {
                        new DiagnosticReport
                        {
                            Id = "duplicate",
                            Meta = new Meta()
                            {
                                Tag = new List<Coding>
                                {
                                    new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "source"),
                                    new Coding(ClinicalReferenceDuplicator.TagIsDuplicate, "true"),
                                },
                            },
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
                                    Url = "http://example.org/fhir/Binary/attachment-duplicate",
                                },
                            },
                        },
                    },
                },
                new object[]
                {
                    // Update a DocumentReference resource without any attachment.
                    new DocumentReference
                    {
                        Id = "source",
                        Meta = new Meta()
                        {
                            Tag = new List<Coding>
                            {
                                new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "duplicate"),
                            },
                        },
                        Content = new List<DocumentReference.ContentComponent>(),
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
                            Meta = new Meta()
                            {
                                Tag = new List<Coding>
                                {
                                    new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "source"),
                                    new Coding(ClinicalReferenceDuplicator.TagIsDuplicate, "true"),
                                },
                            },
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
                                    Url = "http://example.org/fhir/Binary/attachment-duplicate",
                                },
                            },
                        },
                    },
                },
                new object[]
                {
                    // Update a DocumentReference resource with attachments when a duplicate resource doesn't exist.
                    new DocumentReference
                    {
                        Id = "source",
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
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2006-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment-source1",
                                },
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2007-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment-source2",
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
                    new List<Resource>(),
                },
                new object[]
                {
                    // Update a DocumentReference resource with multiple duplicates.
                    new DocumentReference
                    {
                        Id = "source",
                        Meta = new Meta()
                        {
                            Tag = new List<Coding>
                            {
                                new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "duplicate"),
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
                    new List<Resource>
                    {
                        new DiagnosticReport
                        {
                            Id = "duplicate",
                            Meta = new Meta()
                            {
                                Tag = new List<Coding>
                                {
                                    new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "source"),
                                    new Coding(ClinicalReferenceDuplicator.TagIsDuplicate, "true"),
                                },
                            },
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
                                    Url = "http://example.org/fhir/Binary/attachment-duplicate",
                                },
                            },
                        },
                        new DiagnosticReport
                        {
                            Id = "duplicate1",
                            Meta = new Meta()
                            {
                                Tag = new List<Coding>
                                {
                                    new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "source"),
                                    new Coding(ClinicalReferenceDuplicator.TagIsDuplicate, "true"),
                                },
                            },
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
                                    Url = "http://example.org/fhir/Binary/attachment-duplicate1",
                                },
                            },
                        },
                        new DiagnosticReport
                        {
                            Id = "duplicate2",
                            Meta = new Meta()
                            {
                                Tag = new List<Coding>
                                {
                                    new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "source"),
                                    new Coding(ClinicalReferenceDuplicator.TagIsDuplicate, "true"),
                                },
                            },
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
                                    Url = "http://example.org/fhir/Binary/attachment-duplicate2",
                                },
                            },
                        },
                    },
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
                    // Delete a DiagnosticReport resource with one attachment.
                    new DiagnosticReport
                    {
                        Id = "source",
                        Meta = new Meta()
                        {
                            Tag = new List<Coding>
                            {
                                new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "duplicate"),
                            },
                        },
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
                    DeleteOperation.SoftDelete,
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
                                    new Coding(ClinicalReferenceDuplicator.TagIsDuplicate, "true"),
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
                    },
                },
                new object[]
                {
                    // Delete a  DiagnosticReport resource with multiple attachments.
                    new DiagnosticReport
                    {
                        Id = "source",
                        Meta = new Meta()
                        {
                            Tag = new List<Coding>
                            {
                                new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "duplicate"),
                            },
                        },
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
                    DeleteOperation.SoftDelete,
                    new List<Resource>
                    {
                        new DocumentReference
                        {
                            Id = "duplicate",
                            Meta = new Meta()
                            {
                                Tag = new List<Coding>
                                {
                                    new Coding(ClinicalReferenceDuplicator.TagIsDuplicate, "true"),
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
                    },
                },
                new object[]
                {
                    // Delete a DiagnosticReport resource without any attachment.
                    new DiagnosticReport
                    {
                        Id = "source",
                        Meta = new Meta()
                        {
                            Tag = new List<Coding>
                            {
                                new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "duplicate"),
                            },
                        },
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
                        PresentedForm = new List<Attachment>(),
                    },
                    DeleteOperation.SoftDelete,
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
                                    new Coding(ClinicalReferenceDuplicator.TagIsDuplicate, "true"),
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
                    },
                },
                new object[]
                {
                    // Delete a DiagnosticReport resource with attachments when no duplicates.
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
                                Creation = "2007-12-24",
                                Url = "http://example.org/fhir/Binary/attachment-source2",
                            },
                        },
                    },
                    DeleteOperation.SoftDelete,
                    new List<Resource>(),
                },
                new object[]
                {
                    // Delete a DiagnosticReport resource with multiple duplicates.
                    new DiagnosticReport
                    {
                        Id = "source",
                        Meta = new Meta()
                        {
                            Tag = new List<Coding>
                            {
                                new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "duplicate"),
                            },
                        },
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
                    DeleteOperation.SoftDelete,
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
                                    new Coding(ClinicalReferenceDuplicator.TagIsDuplicate, "true"),
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
                                    new Coding(ClinicalReferenceDuplicator.TagIsDuplicate, "true"),
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
                            Id = "duplicate2",
                            Meta = new Meta()
                            {
                                Tag = new List<Coding>
                                {
                                    new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "source"),
                                    new Coding(ClinicalReferenceDuplicator.TagIsDuplicate, "true"),
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
                    },
                },
                new object[]
                {
                    // Delete a DocumentReference resource with one attachment.
                    new DocumentReference
                    {
                        Id = "source",
                        Meta = new Meta()
                        {
                            Tag = new List<Coding>
                            {
                                new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "duplicate"),
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
                    DeleteOperation.HardDelete,
                    new List<Resource>
                    {
                        new DiagnosticReport
                        {
                            Id = "duplicate",
                            Meta = new Meta()
                            {
                                Tag = new List<Coding>
                                {
                                    new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "source"),
                                    new Coding(ClinicalReferenceDuplicator.TagIsDuplicate, "true"),
                                },
                            },
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
                                    Url = "http://example.org/fhir/Binary/attachment-duplicate",
                                },
                            },
                        },
                    },
                },
                new object[]
                {
                    // Delete a DocumentReference resource with multiple attachment.
                    new DocumentReference
                    {
                        Id = "source",
                        Meta = new Meta()
                        {
                            Tag = new List<Coding>
                            {
                                new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "duplicate"),
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
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2006-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment-source1",
                                },
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2007-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment-source2",
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
                    DeleteOperation.HardDelete,
                    new List<Resource>
                    {
                        new DiagnosticReport
                        {
                            Id = "duplicate",
                            Meta = new Meta()
                            {
                                Tag = new List<Coding>
                                {
                                    new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "source"),
                                    new Coding(ClinicalReferenceDuplicator.TagIsDuplicate, "true"),
                                },
                            },
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
                                    Url = "http://example.org/fhir/Binary/attachment-duplicate",
                                },
                            },
                        },
                    },
                },
                new object[]
                {
                    // Delete a DocumentReference resource without any attachment.
                    new DocumentReference
                    {
                        Id = "source",
                        Meta = new Meta()
                        {
                            Tag = new List<Coding>
                            {
                                new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "duplicate"),
                            },
                        },
                        Content = new List<DocumentReference.ContentComponent>(),
                        Subject = new ResourceReference(Guid.NewGuid().ToString()),
#if R4 || R4B || Stu3
                        Status = DocumentReferenceStatus.Current,
#else
                        Status = DocumentReference.DocumentReferenceStatus.Current,
#endif
                    },
                    DeleteOperation.HardDelete,
                    new List<Resource>
                    {
                        new DiagnosticReport
                        {
                            Id = "duplicate",
                            Meta = new Meta()
                            {
                                Tag = new List<Coding>
                                {
                                    new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "source"),
                                    new Coding(ClinicalReferenceDuplicator.TagIsDuplicate, "true"),
                                },
                            },
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
                                    Url = "http://example.org/fhir/Binary/attachment-duplicate",
                                },
                            },
                        },
                    },
                },
                new object[]
                {
                    // Delete a DocumentReference resource with attachments when no duplicates.
                    new DocumentReference
                    {
                        Id = "source",
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
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2006-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment-source1",
                                },
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2007-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment-source2",
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
                    DeleteOperation.HardDelete,
                    new List<Resource>(),
                },
                new object[]
                {
                    // Delete a DocumentReference resource with multiple duplicates.
                    new DocumentReference
                    {
                        Id = "source",
                        Meta = new Meta()
                        {
                            Tag = new List<Coding>
                            {
                                new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "duplicate"),
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
                    DeleteOperation.HardDelete,
                    new List<Resource>
                    {
                        new DiagnosticReport
                        {
                            Id = "duplicate",
                            Meta = new Meta()
                            {
                                Tag = new List<Coding>
                                {
                                    new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "source"),
                                    new Coding(ClinicalReferenceDuplicator.TagIsDuplicate, "true"),
                                },
                            },
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
                                    Url = "http://example.org/fhir/Binary/attachment-duplicate",
                                },
                            },
                        },
                        new DiagnosticReport
                        {
                            Id = "duplicate1",
                            Meta = new Meta()
                            {
                                Tag = new List<Coding>
                                {
                                    new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "source"),
                                    new Coding(ClinicalReferenceDuplicator.TagIsDuplicate, "true"),
                                },
                            },
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
                                    Url = "http://example.org/fhir/Binary/attachment-duplicate",
                                },
                            },
                        },
                        new DiagnosticReport
                        {
                            Id = "duplicate2",
                            Meta = new Meta()
                            {
                                Tag = new List<Coding>
                                {
                                    new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "source"),
                                    new Coding(ClinicalReferenceDuplicator.TagIsDuplicate, "true"),
                                },
                            },
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
                                    Url = "http://example.org/fhir/Binary/attachment-duplicate",
                                },
                            },
                        },
                    },
                },
            };

            foreach (var d in data)
            {
                yield return d;
            }
        }
    }
}
