﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.Mocks;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using ResourceVersionPolicy = Hl7.Fhir.Model.CapabilityStatement.ResourceVersionPolicy;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Conformance
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class ConformanceProviderExtensionTests
    {
        private readonly IConformanceProvider _conformanceProvider;

        public ConformanceProviderExtensionTests()
        {
            _conformanceProvider = Substitute.For<ConformanceProviderBase>();
        }

        [Theory]
        [InlineData(ResourceVersionPolicy.NoVersion, false)]
        [InlineData(ResourceVersionPolicy.Versioned, true)]
        [InlineData(ResourceVersionPolicy.VersionedUpdate, true)]
        public async Task GivenCoreConfigWithVersioningPolicy_WhenCheckingIfKeepHistory_ThenCorrectValueIsReturned(ResourceVersionPolicy versioningPolicy, bool expectedKeepHistory)
        {
            CapabilityStatement statement = CapabilityStatementMock.GetMockedCapabilityStatement();
            CapabilityStatementMock.SetupMockResource(statement, ResourceType.Patient, null, null, versioningPolicy);

            _conformanceProvider.GetCapabilityStatementOnStartup().Returns(statement.ToResourceElement());

            bool actualKeepHistory = await _conformanceProvider.CanKeepHistory(KnownResourceTypes.Patient, CancellationToken.None);
            Assert.Equal(expectedKeepHistory, actualKeepHistory);
        }
    }
}
