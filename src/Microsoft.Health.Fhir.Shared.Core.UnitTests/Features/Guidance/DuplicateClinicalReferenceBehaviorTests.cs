// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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

        [Fact]
        public async Task GivenCreateRequest_WhenResourceIsCreated_ThenDuplicateResourceShouldBeCreated()
        {
            var diagnosticReport = new DiagnosticReport
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
                PresentedForm = new List<Attachment>
                {
                    new Attachment()
                    {
                        ContentType = "application/xhtml",
                        Creation = "2005-12-24",
                        Url = "http://example.org/fhir/Binary/1e404af3-077f-4bee-b7a6-a9be97e1ce32",
                    },
                },
            };

            var resourceElement = diagnosticReport.ToResourceElement();
            var resourceWrapper = CreateResourceWrapper(resourceElement);
            var request = new CreateResourceRequest(resourceElement);
            var response = new UpsertResourceResponse(new SaveOutcome(new RawResourceElement(resourceWrapper), SaveOutcomeType.Created));
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
    }
}
