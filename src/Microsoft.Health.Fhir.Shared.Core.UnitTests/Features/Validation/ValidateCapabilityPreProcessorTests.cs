// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net.Http;
using System.Threading;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.Mocks;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
#pragma warning disable IDE0005 // Using directive is unnecessary.
using static Hl7.Fhir.Model.CapabilityStatement;
#pragma warning restore IDE0005 // Using directive is unnecessary.
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Validation
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class ValidateCapabilityPreProcessorTests
    {
        private readonly IConformanceProvider _conformanceProvider;

        public ValidateCapabilityPreProcessorTests()
        {
            var statement = CapabilityStatementMock.GetMockedCapabilityStatement();
            CapabilityStatementMock.SetupMockResource(statement, ResourceType.Observation, interactions: new[] { TypeRestfulInteraction.Read });

            _conformanceProvider = Substitute.For<ConformanceProviderBase>();
            _conformanceProvider.GetCapabilityStatementOnStartup().Returns(statement.ToTypedElement().ToResourceElement());
        }

        [Fact]
        public async Task GivenARequest_WhenValidatingCapability_ThenAllValidationRulesShouldRun()
        {
            var preProcessor = new ValidateCapabilityPreProcessor<GetResourceRequest, GetResourceResponse>(_conformanceProvider);

            var getResourceRequest = new GetResourceRequest("Observation", Guid.NewGuid().ToString(), bundleResourceContext: null);
            var resource = Samples.GetDefaultObservation().UpdateId("observation1");
            var mockResponse = new GetResourceResponse(CreateRawResourceElement(resource));

            await preProcessor.HandleAsync(getResourceRequest, () => Task.FromResult(mockResponse), CancellationToken.None);
        }

        [Theory]
        [InlineData(DeleteOperation.SoftDelete)]
        [InlineData(DeleteOperation.HardDelete)]
        public async Task GivenARequestNotAllowed_WhenValidatingCapability_ThenMethodNotAllowedExceptionShouldThrow(DeleteOperation deleteOperation)
        {
            var preProcessor = new ValidateCapabilityPreProcessor<DeleteResourceRequest, DeleteResourceResponse>(_conformanceProvider);

            var deleteResourceRequest = new DeleteResourceRequest("Observation", Guid.NewGuid().ToString(), deleteOperation, bundleResourceContext: null);

            await Assert.ThrowsAsync<MethodNotAllowedException>(
                async () => await preProcessor.HandleAsync(
                    deleteResourceRequest,
                    () => Task.FromResult(new DeleteResourceResponse(new ResourceKey("Observation", "test-id"))),
                    CancellationToken.None));
        }

        private static RawResourceElement CreateRawResourceElement(ResourceElement resource)
        {
            var rawResource = new RawResource("data", FhirResourceFormat.Json, isMetaSet: true);
            var wrapper = new ResourceWrapper(
                resource,
                rawResource,
                new ResourceRequest(HttpMethod.Post, "http://fhir"),
                deleted: false,
                searchIndices: null,
                compartmentIndices: null,
                lastModifiedClaims: null);

            return new RawResourceElement(wrapper);
        }
    }
}
