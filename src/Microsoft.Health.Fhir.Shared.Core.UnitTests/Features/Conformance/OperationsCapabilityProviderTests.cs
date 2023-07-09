// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Operations;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Conformance
{
    /// <summary>
    /// shared conformance tests
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public partial class OperationsCapabilityProviderTests
    {
        private IUrlResolver _urlResolver;
        private IOptions<OperationsConfiguration> _operationsOptions = Substitute.For<IOptions<OperationsConfiguration>>();
        private IOptions<FeatureConfiguration> _featureOptions = Substitute.For<IOptions<FeatureConfiguration>>();
        private IOptions<CoreFeatureConfiguration> _coreFeatureOptions = Substitute.For<IOptions<CoreFeatureConfiguration>>();
        private OperationsConfiguration _operationsConfiguration = new OperationsConfiguration();
        private CoreFeatureConfiguration _coreFeatureConfiguration = new CoreFeatureConfiguration();
        private FeatureConfiguration _featureConfiguration = new FeatureConfiguration();

        public OperationsCapabilityProviderTests()
        {
            _urlResolver = Substitute.For<IUrlResolver>();
            _urlResolver.ResolveMetadataUrl(Arg.Any<bool>()).Returns(new System.Uri("https://test.com"));
            _operationsOptions.Value.Returns(_operationsConfiguration);
            _featureOptions.Value.Returns(_featureConfiguration);
            _coreFeatureOptions.Value.Returns(_coreFeatureConfiguration);
        }

        [Fact]
        public void GivenAConformanceBuilder_WhenSupportsSelectableSearchParametersIsEnabled_ThenStatusOperationIsNotAdded()
        {
            _coreFeatureConfiguration.SupportsSelectableSearchParameters = false;
            ListedCapabilityStatement listedCapabilityStatement = new ListedCapabilityStatement();
            OperationsCapabilityProvider provider = new OperationsCapabilityProvider(_operationsOptions, _featureOptions, _coreFeatureOptions, _urlResolver);
            ICapabilityStatementBuilder builder = Substitute.For<ICapabilityStatementBuilder>();
            provider.Build(builder);

            builder.Apply(Arg.Any<Action<ListedCapabilityStatement>>()).Received(3);
        }
    }
}
