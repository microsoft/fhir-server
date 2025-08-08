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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Guidance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Delete;
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
        private readonly CoreFeatureConfiguration _coreFeatureConfiguration;
        private readonly IClinicalReferenceDuplicator _duplicator;
        private readonly IRawResourceFactory _rawResourceFactory;
        private readonly IResourceWrapperFactory _resourceWrapperFactory;
        private readonly ILogger<DuplicateClinicalReferenceBehavior> _logger;

        public DuplicateClinicalReferenceBehaviorTests()
        {
            _duplicator = Substitute.For<IClinicalReferenceDuplicator>();
            _duplicator.CheckDuplicate(Arg.Any<ResourceKey>()).Returns(true);
            _duplicator.ShouldDuplicate(Arg.Any<Resource>()).Returns(true);

            _logger = Substitute.For<ILogger<DuplicateClinicalReferenceBehavior>>();
            _coreFeatureConfiguration = new CoreFeatureConfiguration
            {
                EnableClinicalReferenceDuplication = true,
            };

            _rawResourceFactory = Substitute.For<RawResourceFactory>(new FhirJsonSerializer());
            _resourceWrapperFactory = Substitute.For<IResourceWrapperFactory>();
            _resourceWrapperFactory
                .Create(Arg.Any<ResourceElement>(), Arg.Any<bool>(), Arg.Any<bool>())
                .Returns(x => CreateResourceWrapper(x.ArgAt<ResourceElement>(0), x.ArgAt<bool>(1)));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenCreateRequest_WhenDuplicateClinicalReferenceIsEnabled_ThenDuplicateResourceShouldBeCreated(
            bool enabled)
        {
            var resource = new DiagnosticReport()
            {
                Id = Guid.NewGuid().ToString(),
                Status = DiagnosticReport.DiagnosticReportStatus.Registered,
            };

            var resourceElement = resource.ToResourceElement();
            var resourceWrapper = CreateResourceWrapper(resourceElement);
            var request = new CreateResourceRequest(resourceElement);
            var response = new UpsertResourceResponse(
                new SaveOutcome(new RawResourceElement(resourceWrapper), SaveOutcomeType.Created));

            _coreFeatureConfiguration.EnableClinicalReferenceDuplication = enabled;
            var behavior = new DuplicateClinicalReferenceBehavior(
                Options.Create(_coreFeatureConfiguration),
                _duplicator,
                _logger);

            await behavior.Handle(
                request,
                async (ct) => await Task.Run(() => response),
                CancellationToken.None);

            _duplicator.Received(enabled ? 1 : 0).ShouldDuplicate(Arg.Any<Resource>());
            await _duplicator.Received(enabled ? 1 : 0).CreateResourceAsync(
                Arg.Any<Resource>(),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, false)]
        public async Task GivenUpsertRequest_WhenDuplicateClinicalReferenceIsEnabled_ThenThenDuplicateResourceShouldBeUpdated(
            bool enabled,
            bool duplicateFound)
        {
            var resource = new DiagnosticReport()
            {
                Id = Guid.NewGuid().ToString(),
                Status = DiagnosticReport.DiagnosticReportStatus.Registered,
            };

            var resourceElement = resource.ToResourceElement();
            var resourceWrapper = CreateResourceWrapper(resourceElement);
            var request = new UpsertResourceRequest(resourceElement);
            var response = new UpsertResourceResponse(
                new SaveOutcome(new RawResourceElement(resourceWrapper), SaveOutcomeType.Created));

            var duplicateResources = new List<Resource>();
            if (duplicateFound)
            {
                duplicateResources.Add(resource);
            }

            _duplicator.UpdateResourceAsync(
                Arg.Any<Resource>(),
                Arg.Any<CancellationToken>())
                .Returns(
                    x =>
                    {
                        return Task.FromResult<IReadOnlyList<Resource>>(duplicateResources);
                    });

            _coreFeatureConfiguration.EnableClinicalReferenceDuplication = enabled;
            var behavior = new DuplicateClinicalReferenceBehavior(
                Options.Create(_coreFeatureConfiguration),
                _duplicator,
                _logger);

            await behavior.Handle(
                request,
                async (ct) => await Task.Run(() => response),
                CancellationToken.None);

            _duplicator.Received(enabled ? 1 : 0).ShouldDuplicate(Arg.Any<Resource>());
            await _duplicator.Received(enabled ? 1 : 0).UpdateResourceAsync(
                Arg.Any<Resource>(),
                Arg.Any<CancellationToken>());
            await _duplicator.Received(enabled && !duplicateFound ? 1 : 0).CreateResourceAsync(
                Arg.Any<Resource>(),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenDeleteRequest_WhenDuplicateClinicalReferenceIsEnabled_ThenThenDuplicateResourceShouldBeDeleted(
            bool enabled)
        {
            var resource = new DiagnosticReport()
            {
                Id = Guid.NewGuid().ToString(),
                Status = DiagnosticReport.DiagnosticReportStatus.Registered,
            };

            var resourceElement = resource.ToResourceElement();
            var resourceWrapper = CreateResourceWrapper(resourceElement);
            var request = new DeleteResourceRequest(
                new ResourceKey(resource.TypeName, resource.Id),
                DeleteOperation.SoftDelete);
            var response = new DeleteResourceResponse(
                new ResourceKey(resource.TypeName, resource.Id));

            _coreFeatureConfiguration.EnableClinicalReferenceDuplication = enabled;
            var behavior = new DuplicateClinicalReferenceBehavior(
                Options.Create(_coreFeatureConfiguration),
                _duplicator,
                _logger);

            await behavior.Handle(
                request,
                async (ct) => await Task.Run(() => response),
                CancellationToken.None);

            _duplicator.Received(enabled ? 1 : 0).CheckDuplicate(Arg.Any<ResourceKey>());
            await _duplicator.Received(enabled ? 1 : 0).DeleteResourceAsync(
                Arg.Any<ResourceKey>(),
                Arg.Any<DeleteOperation>(),
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
    }
}
