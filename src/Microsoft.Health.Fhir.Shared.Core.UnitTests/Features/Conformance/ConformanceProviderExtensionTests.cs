// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Tests.Common.Mocks;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Conformance
{
    public class ConformanceProviderExtensionTests
    {
        private readonly IConformanceProvider _conformanceProvider;

        public ConformanceProviderExtensionTests()
        {
            _conformanceProvider = Substitute.For<ConformanceProviderBase>();
        }

        [Fact]
        public async void GivenCoreConfigWithNoVersionVersioningPolicy_WhenCheckingIfKeepHistory_ThenFalseIsReturned()
        {
            const ResourceType resourceType = ResourceType.Patient;

            CapabilityStatement statement = CapabilityStatementMock.GetMockedCapabilityStatement();
            CapabilityStatementMock.SetupMockResource(statement, resourceType, null, null, CapabilityStatement.ResourceVersionPolicy.NoVersion);

            _conformanceProvider.GetCapabilityStatementOnStartup().Returns(statement.ToResourceElement());

            bool keepHistory = await _conformanceProvider.CanKeepHistory(resourceType.ToString(), CancellationToken.None);
            Assert.False(keepHistory);
        }

        [Fact]
        public async void GivenCoreConfigWithVersionedVersioningPolicy_WhenCheckingIfKeepHistory_ThenTrueIsReturned()
        {
            const ResourceType resourceType = ResourceType.Patient;

            CapabilityStatement statement = CapabilityStatementMock.GetMockedCapabilityStatement();
            CapabilityStatementMock.SetupMockResource(statement, resourceType, null, null, CapabilityStatement.ResourceVersionPolicy.Versioned);

            _conformanceProvider.GetCapabilityStatementOnStartup().Returns(statement.ToResourceElement());

            bool keepHistory = await _conformanceProvider.CanKeepHistory(resourceType.ToString(), CancellationToken.None);
            Assert.True(keepHistory);
        }

        [Fact]
        public async void GivenCoreConfigWithVersionedUpdateVersioningPolicy_WhenCheckingIfKeepHistory_ThenTrueIsReturned()
        {
            const ResourceType resourceType = ResourceType.Patient;

            CapabilityStatement statement = CapabilityStatementMock.GetMockedCapabilityStatement();
            CapabilityStatementMock.SetupMockResource(statement, resourceType, null, null, CapabilityStatement.ResourceVersionPolicy.VersionedUpdate);

            _conformanceProvider.GetCapabilityStatementOnStartup().Returns(statement.ToResourceElement());

            bool keepHistory = await _conformanceProvider.CanKeepHistory(resourceType.ToString(), CancellationToken.None);
            Assert.True(keepHistory);
        }
    }
}
