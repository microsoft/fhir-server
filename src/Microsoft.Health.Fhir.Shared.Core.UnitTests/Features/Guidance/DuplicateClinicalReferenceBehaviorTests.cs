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
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Shared.Core.Features.Guidance;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Guidance
{
    public class DuplicateClinicalReferenceBehaviorTests
    {
        private readonly DuplicateClinicalReferenceBehavior _behavior;
        private readonly IMediator _mediator;
        private readonly ISearchService _searchService;
        private readonly CoreFeatureConfiguration _coreFeatureConfiguration;
        private readonly ILogger<DuplicateClinicalReferenceBehavior> _logger;
        private readonly IRawResourceFactory _rawResourceFactory;
        private readonly IResourceWrapperFactory _resourceWrapperFactory;

        public DuplicateClinicalReferenceBehaviorTests()
        {
            _mediator = Substitute.For<IMediator>();
            _searchService = Substitute.For<ISearchService>();
            _logger = Substitute.For<ILogger<DuplicateClinicalReferenceBehavior>>();
            _coreFeatureConfiguration = new CoreFeatureConfiguration
            {
                EnableClinicalReferenceDuplication = true,
            };

            _behavior = new DuplicateClinicalReferenceBehavior(
                _mediator,
                _searchService,
                Options.Create(_coreFeatureConfiguration),
                _logger);

            _rawResourceFactory = Substitute.For<RawResourceFactory>(new FhirJsonSerializer());
            _resourceWrapperFactory = Substitute.For<IResourceWrapperFactory>();
            _resourceWrapperFactory
                .Create(Arg.Any<ResourceElement>(), Arg.Any<bool>(), Arg.Any<bool>())
                .Returns(x => CreateResourceWrapper(x.ArgAt<ResourceElement>(0), x.ArgAt<bool>(1)));
        }

        [Theory]
        [MemberData(nameof(GetCreateResourceData))]
        public async Task GivenCreateRequest_WhenResourceIsCreated_ThenDuplicateResourceShouldBeCreated(
            Resource resource,
            bool shouldDuplicate)
        {
            var resourceType = resource.TypeName;
            var duplicateResourceType = string.Equals(resource, KnownResourceTypes.DiagnosticReport)
                ? KnownResourceTypes.DocumentReference : KnownResourceTypes.DiagnosticReport;

            // Set up a validation on a request for creating the original resource.
            var resourceElement = resource.ToResourceElement();
            var resourceWrapper = CreateResourceWrapper(resourceElement);
            var request = new CreateResourceRequest(resourceElement);
            var response = new UpsertResourceResponse(
                new SaveOutcome(new RawResourceElement(resourceWrapper), SaveOutcomeType.Created));
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
                        Assert.True(shouldDuplicate, "Duplicating a resource is unnecessary.");

                        var re = ((CreateResourceRequest)x[0]).Resource;
                        Assert.NotNull(re);
                        Assert.Equal(duplicateResourceType, re.InstanceType, true);

                        var r = re.ToPoco();
                        Assert.NotNull(r.Meta?.Tag);
                        Assert.Contains(
                            r.Meta.Tag,
                            x => string.Equals(x.System, DuplicateClinicalReferenceBehavior.TagDuplicateOf, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(x.Code, resource.Id, StringComparison.OrdinalIgnoreCase));

                        if (string.Equals(duplicateResourceType, KnownResourceTypes.DiagnosticReport, StringComparison.OrdinalIgnoreCase))
                        {
                            var original = (DocumentReference)resource;
                            var duplicate = (DiagnosticReport)r;
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

            // Set up a validation on a request for updating the initial resource with the id of the duplicate resource.
            _mediator.Send<UpsertResourceResponse>(
                Arg.Is<UpsertResourceRequest>(x => string.Equals(x.Resource.InstanceType, KnownResourceTypes.DiagnosticReport, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>())
                .Returns(
                    x =>
                    {
                        Assert.True(shouldDuplicate, "Duplicating a resource is unnecessary.");

                        var re = ((UpsertResourceRequest)x[0]).Resource;
                        Assert.NotNull(re);
                        Assert.Equal(KnownResourceTypes.DiagnosticReport, re.InstanceType, true);

                        var r = re.ToPoco();
                        Assert.NotNull(r.Meta?.Tag);
                        Assert.Contains(
                            r.Meta.Tag,
                            t => string.Equals(t.System, DuplicateClinicalReferenceBehavior.TagDuplicateOf, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(t.Code, duplicateResource.Id, StringComparison.OrdinalIgnoreCase));

                        return Task.FromResult(
                            new UpsertResourceResponse(
                                new SaveOutcome(new RawResourceElement(CreateResourceWrapper(r.ToResourceElement())), SaveOutcomeType.Updated)));
                    });

            await _behavior.Handle(request, async (ct) => await Task.Run(() => response), CancellationToken.None);
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
                    true,
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
                    true,
                },
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
                    },
                    false,
                },
            };

            foreach (var d in data)
            {
                yield return d;
            }
        }
    }
}
