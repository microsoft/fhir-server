// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Messages.Get;
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
            var preProcessor = new ValidateCapabilityPreProcessor<GetResourceRequest>(_conformanceProvider);

            var getResourceRequest = new GetResourceRequest("Observation", Guid.NewGuid().ToString(), bundleResourceContext: null);

            await preProcessor.Process(getResourceRequest, CancellationToken.None);
        }

        [Theory]
        [InlineData(DeleteOperation.SoftDelete)]
        [InlineData(DeleteOperation.HardDelete)]
        public async Task GivenARequestNotAllowed_WhenValidatingCapability_ThenMethodNotAllowedExceptionShouldThrow(DeleteOperation deleteOperation)
        {
            var preProcessor = new ValidateCapabilityPreProcessor<DeleteResourceRequest>(_conformanceProvider);

            var deleteResourceRequest = new DeleteResourceRequest("Observation", Guid.NewGuid().ToString(), deleteOperation, bundleResourceContext: null);

            await Assert.ThrowsAsync<MethodNotAllowedException>(async () => await preProcessor.Process(deleteResourceRequest, CancellationToken.None));
        }
    }
}
