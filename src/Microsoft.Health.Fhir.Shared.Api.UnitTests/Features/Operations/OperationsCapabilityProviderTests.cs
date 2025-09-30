// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Operations;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Operations
{
    /// <summary>
    /// shared conformance tests
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class OperationsCapabilityProviderTests
    {
        private readonly IUrlResolver _urlResolver;
        private readonly IOptions<OperationsConfiguration> _operationsOptions = Substitute.For<IOptions<OperationsConfiguration>>();
        private readonly IOptions<FeatureConfiguration> _featureOptions = Substitute.For<IOptions<FeatureConfiguration>>();
        private readonly IOptions<CoreFeatureConfiguration> _coreFeatureOptions = Substitute.For<IOptions<CoreFeatureConfiguration>>();
        private readonly IOptions<ImplementationGuidesConfiguration> _implementationGuidesOptions = Substitute.For<IOptions<ImplementationGuidesConfiguration>>();
        private readonly OperationsConfiguration _operationsConfiguration = new();
        private readonly CoreFeatureConfiguration _coreFeatureConfiguration = new();
        private readonly FeatureConfiguration _featureConfiguration = new();
        private readonly IFhirRuntimeConfiguration _fhirRuntimeConfiguration;

        public OperationsCapabilityProviderTests()
        {
            _urlResolver = Substitute.For<IUrlResolver>();
            _urlResolver.ResolveMetadataUrl(Arg.Any<bool>()).Returns(new System.Uri("https://test.com"));
            _operationsOptions.Value.Returns(_operationsConfiguration);
            _featureOptions.Value.Returns(_featureConfiguration);
            _coreFeatureOptions.Value.Returns(_coreFeatureConfiguration);
            _fhirRuntimeConfiguration = Substitute.For<IFhirRuntimeConfiguration>();
            _implementationGuidesOptions.Value.Returns(new ImplementationGuidesConfiguration());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenAConformanceBuilder_WhenCallingOperationsCapabilityForSelectableSearchParameters_ThenStatusOperationIsAddedWhenEnabled(bool added)
        {
            _coreFeatureConfiguration.SupportsSelectableSearchParameters = added;

            var provider = new OperationsCapabilityProvider(_operationsOptions, _featureOptions, _coreFeatureOptions, _implementationGuidesOptions, _urlResolver, _fhirRuntimeConfiguration);
            ICapabilityStatementBuilder builder = Substitute.For<ICapabilityStatementBuilder>();
            await provider.BuildAsync(builder, CancellationToken.None);

            builder.Received(added ? 1 : 0)
                .Apply(Arg.Is<Action<ListedCapabilityStatement>>(x => x.Method.Name == nameof(OperationsCapabilityProvider.AddSelectableSearchParameterDetails)));
        }

        [Theory]
        [InlineData(KnownDataStores.SqlServer, true)]
        [InlineData(KnownDataStores.SqlServer, false)]
        [InlineData(KnownDataStores.CosmosDb, true)]
        [InlineData(KnownDataStores.CosmosDb, false)]
        public async Task GivenAConformanceBuilder_WhenCallingOperationsCapabilityForIncludes_ThenIncludesDetailsIsAddedWhenSqlServerAndSupported(string dataStore, bool support)
        {
            _fhirRuntimeConfiguration.DataStore.Returns(dataStore);
            _coreFeatureConfiguration.SupportsIncludes = support;

            var provider = new OperationsCapabilityProvider(_operationsOptions, _featureOptions, _coreFeatureOptions, _implementationGuidesOptions, _urlResolver, _fhirRuntimeConfiguration);
            ICapabilityStatementBuilder builder = Substitute.For<ICapabilityStatementBuilder>();
            await provider.BuildAsync(builder, CancellationToken.None);

            builder.Received(support && dataStore == KnownDataStores.SqlServer ? 1 : 0)
                .Apply(Arg.Is<Action<ListedCapabilityStatement>>(x => x.Method.Name == nameof(OperationsCapabilityProvider.AddIncludesDetails)));
        }
    }
}
