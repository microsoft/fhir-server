// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common.Mocks;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Validation
{
    public class ValidateCapabilityPreProcessorTests
    {
        private readonly IConformanceProvider _conformanceProvider;

        public ValidateCapabilityPreProcessorTests()
        {
            var statement = CapabilityStatementMock.GetMockedCapabilityStatement();
            CapabilityStatementMock.SetupMockResource(statement, ResourceType.Observation, new[] { CapabilityStatement.TypeRestfulInteraction.Read });

            _conformanceProvider = Substitute.For<ConformanceProviderBase>();
            _conformanceProvider.GetCapabilityStatementAsync().Returns(new ResourceElement(statement.ToTypedElement()));
        }

        [Fact]
        public async Task GivenARequest_WhenValidatingCapability_ThenAllValidationRulesShouldRun()
        {
            var preProcessor = new ValidateCapabilityPreProcessor<GetResourceRequest>(_conformanceProvider);

            var getResourceRequest = new GetResourceRequest("Observation", Guid.NewGuid().ToString());

            await preProcessor.Process(getResourceRequest, CancellationToken.None);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GivenARequestNotAllowed_WhenValidatingCapability_ThenMethodNotAllowedExceptionShouldThrow(bool hardDelete)
        {
            var preProcessor = new ValidateCapabilityPreProcessor<DeleteResourceRequest>(_conformanceProvider);

            var deleteResourceRequest = new DeleteResourceRequest("Observation", Guid.NewGuid().ToString(), hardDelete);

            await Assert.ThrowsAsync<MethodNotAllowedException>(async () => await preProcessor.Process(deleteResourceRequest, CancellationToken.None));
        }
    }
}
