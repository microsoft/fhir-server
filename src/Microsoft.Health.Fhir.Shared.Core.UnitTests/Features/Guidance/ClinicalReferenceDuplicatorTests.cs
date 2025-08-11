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
        private readonly IMediator _mediator;
        private readonly ISearchService _searchService;
        private readonly IRawResourceFactory _rawResourceFactory;
        private readonly IResourceWrapperFactory _resourceWrapperFactory;
        private readonly ILogger<ClinicalReferenceDuplicator> _logger;

        public ClinicalReferenceDuplicatorTests()
        {
            _mediator = Substitute.For<IMediator>();
            _searchService = Substitute.For<ISearchService>();
            _logger = Substitute.For<ILogger<ClinicalReferenceDuplicator>>();

            _duplicator = new ClinicalReferenceDuplicator(
                _mediator,
                _searchService,
                _logger);

            _rawResourceFactory = Substitute.For<RawResourceFactory>(new FhirJsonSerializer());
            _resourceWrapperFactory = Substitute.For<IResourceWrapperFactory>();
            _resourceWrapperFactory
                .Create(Arg.Any<ResourceElement>(), Arg.Any<bool>(), Arg.Any<bool>())
                .Returns(x => CreateResourceWrapper(x.ArgAt<ResourceElement>(0), x.ArgAt<bool>(1)));
        }

        [Theory]
        [MemberData(nameof(GetCreateResourceData))]
        public async Task GivenResource_WhenCreating_ThenDuplicateResourceShouldBeCreated(
            Resource resource)
        {
            var resourceType = resource.TypeName;
            var duplicateResourceType = string.Equals(resourceType, KnownResourceTypes.DiagnosticReport)
                ? KnownResourceTypes.DocumentReference : KnownResourceTypes.DiagnosticReport;

            // Set up a validation on a request for creating the original resource.
            var resourceElement = resource.ToResourceElement();
            _mediator.Send<UpsertResourceResponse>(
                Arg.Is<CreateResourceRequest>(x => string.Equals(x.Resource.InstanceType, resourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>())
                .Throws(new Exception($"Shouldn't be called to create a {resourceType} resource."));

            // Set up a validation on a request for creating a duplicate resource.
            Resource duplicateResource = null;
            _mediator.Send<UpsertResourceResponse>(
                Arg.Is<CreateResourceRequest>(x => string.Equals(x.Resource.InstanceType, duplicateResourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>())
                .Returns(
                    x =>
                    {
                        var re = ((CreateResourceRequest)x[0]).Resource;
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
                            var original = (DocumentReference)resource;
                            var duplicate = (DiagnosticReport)r;

                            Assert.Equal(original.Subject?.Reference, duplicate.Subject?.Reference);
                            Assert.NotNull(duplicate.PresentedForm);
                            foreach (var a in original.Content.Select(x => x.Attachment))
                            {
                                Assert.Contains(
                                    duplicate.PresentedForm,
                                    x => string.Equals(x.Url, a.Url, StringComparison.OrdinalIgnoreCase));
                            }

                            duplicateResource = duplicate;
                        }
                        else
                        {
                            var original = (DiagnosticReport)resource;
                            var duplicate = (DocumentReference)r;

                            Assert.Equal(original.Subject?.Reference, duplicate.Subject?.Reference);
                            Assert.NotNull(duplicate.Content);
                            foreach (var a in original.PresentedForm)
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
                            new UpsertResourceResponse(
                                new SaveOutcome(new RawResourceElement(CreateResourceWrapper(r.ToResourceElement())), SaveOutcomeType.Created)));
                    });

            // Set up a validation on a request for updating the original resource with the id of the duplicate resource.
            _mediator.Send<UpsertResourceResponse>(
                Arg.Is<UpsertResourceRequest>(x => string.Equals(x.Resource.InstanceType, resourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>())
                .Returns(
                    x =>
                    {
                        var re = ((UpsertResourceRequest)x[0]).Resource;
                        Assert.NotNull(re);
                        Assert.Equal(resourceType, re.InstanceType, true);

                        var r = re.ToPoco();
                        Assert.NotNull(r.Meta?.Tag);
                        Assert.Contains(
                            r.Meta.Tag,
                            t => string.Equals(t.System, ClinicalReferenceDuplicator.TagDuplicateOf, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(t.Code, duplicateResource.Id, StringComparison.OrdinalIgnoreCase));

                        return Task.FromResult(
                            new UpsertResourceResponse(
                                new SaveOutcome(new RawResourceElement(CreateResourceWrapper(r.ToResourceElement())), SaveOutcomeType.Updated)));
                    });

            await _duplicator.CreateResourceAsync(
                resource,
                CancellationToken.None);

            // Check how many times create/update was invoked.
            await _mediator.Received(1).Send<UpsertResourceResponse>(
                Arg.Any<CreateResourceRequest>(),
                Arg.Any<CancellationToken>());
            await _mediator.Received(1).Send<UpsertResourceResponse>(
                Arg.Any<UpsertResourceRequest>(),
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

            // Set up a validation on a request for creating a duplicate resource.
            Resource duplicateResource = null;
            _mediator.Send<UpsertResourceResponse>(
                Arg.Is<CreateResourceRequest>(x => string.Equals(x.Resource.InstanceType, duplicateResourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>())
                .Returns(
                    x =>
                    {
                        var re = ((CreateResourceRequest)x[0]).Resource;
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
                            var original = (DocumentReference)resource;
                            var duplicate = (DiagnosticReport)r;

                            Assert.Equal(original.Subject?.Reference, duplicate.Subject?.Reference);
                            Assert.NotNull(duplicate.PresentedForm);
                            foreach (var a in original.Content.Select(x => x.Attachment))
                            {
                                Assert.Contains(
                                    duplicate.PresentedForm,
                                    x => string.Equals(x.Url, a.Url, StringComparison.OrdinalIgnoreCase));
                            }

                            duplicateResource = duplicate;
                        }
                        else
                        {
                            var original = (DiagnosticReport)resource;
                            var duplicate = (DocumentReference)r;

                            Assert.Equal(original.Subject?.Reference, duplicate.Subject?.Reference);
                            Assert.NotNull(duplicate.Content);
                            foreach (var a in original.PresentedForm)
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
                            new UpsertResourceResponse(
                                new SaveOutcome(new RawResourceElement(CreateResourceWrapper(r.ToResourceElement())), SaveOutcomeType.Created)));
                    });

            // Set up a validation on a request for updating the original resource with the id of the duplicate resource.
            _mediator.Send<UpsertResourceResponse>(
                Arg.Is<UpsertResourceRequest>(x => string.Equals(x.Resource.InstanceType, resourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>())
                .Returns(
                    x =>
                    {
                        var re = ((UpsertResourceRequest)x[0]).Resource;
                        Assert.NotNull(re);
                        Assert.Equal(resourceType, re.InstanceType, true);

                        var r = re.ToPoco();
                        Assert.NotNull(r.Meta?.Tag);
                        Assert.Contains(
                            r.Meta.Tag,
                            t => string.Equals(t.System, ClinicalReferenceDuplicator.TagDuplicateOf, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(t.Code, duplicateResource.Id, StringComparison.OrdinalIgnoreCase));

                        return Task.FromResult(
                            new UpsertResourceResponse(
                                new SaveOutcome(new RawResourceElement(CreateResourceWrapper(r.ToResourceElement())), SaveOutcomeType.Updated)));
                    });

            // Set up a validation on a request for updating the duplicate resource.
            _mediator.Send<UpsertResourceResponse>(
                Arg.Is<UpsertResourceRequest>(x => string.Equals(x.Resource.InstanceType, duplicateResourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>())
                .Returns(
                    x =>
                    {
                        var re = ((UpsertResourceRequest)x[0]).Resource;
                        Assert.NotNull(re);
                        Assert.Equal(duplicateResourceType, re.InstanceType, true);

                        var r = re.ToPoco();
                        Assert.NotNull(r.Meta?.Tag);
                        Assert.Contains(
                            r.Meta.Tag,
                            t => string.Equals(t.System, ClinicalReferenceDuplicator.TagDuplicateOf, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(t.Code, resource.Id, StringComparison.OrdinalIgnoreCase));

                        if (string.Equals(duplicateResourceType, KnownResourceTypes.DiagnosticReport, StringComparison.OrdinalIgnoreCase))
                        {
                            var original = (DocumentReference)resource;
                            var duplicate = (DiagnosticReport)r;

                            Assert.Equal(original.Subject?.Reference, duplicate.Subject?.Reference);
                            Assert.NotNull(duplicate.PresentedForm);

                            if (original.Content?.Any(x => !string.IsNullOrEmpty(x.Attachment?.Url)) ?? false)
                            {
                                foreach (var a in original.Content.Select(x => x.Attachment))
                                {
                                    Assert.Contains(
                                        duplicate.PresentedForm,
                                        x => string.Equals(x.Url, a.Url, StringComparison.OrdinalIgnoreCase));
                                }
                            }
                            else
                            {
                                Assert.Equal(0, duplicate.PresentedForm.Count(x => !string.IsNullOrEmpty(x.Url)));
                            }

                            duplicateResource = duplicate;
                        }
                        else
                        {
                            var original = (DiagnosticReport)resource;
                            var duplicate = (DocumentReference)r;

                            Assert.Equal(original.Subject?.Reference, duplicate.Subject?.Reference);
                            Assert.NotNull(duplicate.Content);

                            if (original.PresentedForm?.Any(x => !string.IsNullOrEmpty(x.Url)) ?? false)
                            {
                                foreach (var a in original.PresentedForm)
                                {
                                    Assert.Contains(
                                        duplicate.Content,
                                        x => string.Equals(x.Attachment?.Url, a.Url, StringComparison.OrdinalIgnoreCase));
                                }
                            }
                            else
                            {
                                Assert.Equal(0, duplicate.Content.Count(x => !string.IsNullOrEmpty(x.Attachment?.Url)));
                            }

                            duplicateResource = duplicate;
                        }

                        return Task.FromResult(
                            new UpsertResourceResponse(
                                new SaveOutcome(new RawResourceElement(CreateResourceWrapper(r.ToResourceElement())), SaveOutcomeType.Updated)));
                    });

            await _duplicator.UpdateResourceAsync(
                resource,
                CancellationToken.None);

            // Check how many times create/update was invoked.
            await _searchService.Received(1).SearchAsync(
                Arg.Is<string>(x => string.Equals(x, duplicateResourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>());
            await _mediator.Received(duplicateResources.Any() ? 0 : 1).Send<UpsertResourceResponse>(
                Arg.Any<CreateResourceRequest>(),
                Arg.Any<CancellationToken>());
            await _mediator.Received(duplicateResources.Any() ? 0 : 1).Send<UpsertResourceResponse>(
                Arg.Is<UpsertResourceRequest>(x => string.Equals(x.Resource.InstanceType, resourceType, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>());
            await _mediator.Received(duplicateResources.Any() ? duplicateResources.Count : 0).Send<UpsertResourceResponse>(
                Arg.Is<UpsertResourceRequest>(x => string.Equals(x.Resource.InstanceType, duplicateResourceType, StringComparison.OrdinalIgnoreCase)),
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
                        Id = "original",
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
                                Url = "http://example.org/fhir/Binary/attachment-original",
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
                                    new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "original"),
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
                        Id = "original",
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
                                Url = "http://example.org/fhir/Binary/attachment-original",
                            },
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2006-12-24",
                                Url = "http://example.org/fhir/Binary/attachment-original1",
                            },
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2006-12-24",
                                Url = "http://example.org/fhir/Binary/attachment-original2",
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
                                    new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "original"),
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
                        Id = "original",
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
                                    new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "original"),
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
                        Id = "original",
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
                                Url = "http://example.org/fhir/Binary/attachment-original",
                            },
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2006-12-24",
                                Url = "http://example.org/fhir/Binary/attachment-original1",
                            },
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2007-12-24",
                                Url = "http://example.org/fhir/Binary/attachment-original2",
                            },
                        },
                    },
                    new List<Resource>(),
                },
                new object[]
                {
                    // Update a DocumentReference resource with one attachment.
                    new DocumentReference
                    {
                        Id = "original",
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
                                    Url = "http://example.org/fhir/Binary/attachment-original",
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
                                    new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "original"),
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
                        Id = "original",
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
                                    Url = "http://example.org/fhir/Binary/attachment-original",
                                },
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2006-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment-original1",
                                },
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2007-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment-original2",
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
                                    new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "original"),
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
                        Id = "original",
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
                                    new Coding(ClinicalReferenceDuplicator.TagDuplicateOf, "original"),
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
                        Id = "original",
                        Content = new List<DocumentReference.ContentComponent>
                        {
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2005-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment-original",
                                },
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2006-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment-original1",
                                },
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2007-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment-original2",
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
            };

            foreach (var d in data)
            {
                yield return d;
            }
        }
    }
}
