// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Operations;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Operations;
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
        private const string OperationDefinitionUrl = "http://h7.org/fhir/OperationDefinition/example";

        private readonly IUrlResolver _urlResolver;
        private readonly IOptions<OperationsConfiguration> _operationsOptions = Substitute.For<IOptions<OperationsConfiguration>>();
        private readonly IOptions<FeatureConfiguration> _featureOptions = Substitute.For<IOptions<FeatureConfiguration>>();
        private readonly IOptions<CoreFeatureConfiguration> _coreFeatureOptions = Substitute.For<IOptions<CoreFeatureConfiguration>>();
        private readonly IOptions<ImplementationGuidesConfiguration> _implementationGuidesOptions = Substitute.For<IOptions<ImplementationGuidesConfiguration>>();
        private readonly OperationsConfiguration _operationsConfiguration = new();
        private readonly CoreFeatureConfiguration _coreFeatureConfiguration = new();
        private readonly FeatureConfiguration _featureConfiguration = new();
        private readonly ImplementationGuidesConfiguration _implementationGuidesConfiguration = new();
        private readonly IFhirRuntimeConfiguration _fhirRuntimeConfiguration;

        public OperationsCapabilityProviderTests()
        {
            _urlResolver = Substitute.For<IUrlResolver>();
            _urlResolver.ResolveMetadataUrl(Arg.Any<bool>()).Returns(new System.Uri("https://test.com"));
            _urlResolver.ResolveOperationDefinitionUrl(Arg.Any<string>()).Returns(new System.Uri(OperationDefinitionUrl));
            _operationsOptions.Value.Returns(_operationsConfiguration);
            _featureOptions.Value.Returns(_featureConfiguration);
            _coreFeatureOptions.Value.Returns(_coreFeatureConfiguration);
            _fhirRuntimeConfiguration = Substitute.For<IFhirRuntimeConfiguration>();
            _implementationGuidesOptions.Value.Returns(_implementationGuidesConfiguration);
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

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenConfiguration_WhenBuildingCapabilityStatement_ThenAddExportDetailsShouldBeCalled(
            bool enabled)
        {
            _operationsConfiguration.Export.Enabled = enabled;

            var provider = new OperationsCapabilityProvider(
                _operationsOptions,
                _featureOptions,
                _coreFeatureOptions,
                _implementationGuidesOptions,
                _urlResolver,
                _fhirRuntimeConfiguration);
            ICapabilityStatementBuilder builder = Substitute.For<ICapabilityStatementBuilder>();
            await provider.BuildAsync(builder, CancellationToken.None);

            builder.Received(enabled ? 1 : 0)
                .Apply(Arg.Is<Action<ListedCapabilityStatement>>(x => x.Method.Name == nameof(OperationsCapabilityProvider.AddExportDetails)));
        }

        [Fact]
        public void GivenProvider_WhenAddingDetails_ThenExportOperationsShouldBeAdded()
        {
            var provider = new OperationsCapabilityProvider(
                _operationsOptions,
                _featureOptions,
                _coreFeatureOptions,
                _implementationGuidesOptions,
                _urlResolver,
                _fhirRuntimeConfiguration);
            var restComponent = new ListedRestComponent()
            {
                Mode = ListedCapabilityStatement.ServerMode,
            };

            var capabilityStatement = new ListedCapabilityStatement();
            capabilityStatement.Rest.Add(restComponent);

            provider.AddExportDetails(capabilityStatement);
            var expected = new[]
            {
                OperationsConstants.Export,
                OperationsConstants.PatientExport,
                OperationsConstants.GroupExport,
            };

            Assert.Equal(expected.Length, restComponent.Operation.Count);
            Assert.All(
                expected,
                x =>
                {
                    Assert.Contains(
                        restComponent.Operation,
                        y =>
                        {
                            return string.Equals(x, y.Name, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(OperationDefinitionUrl, y.Definition?.Reference, StringComparison.OrdinalIgnoreCase);
                        });
                });
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenConfiguration_WhenBuildingCapabilityStatement_ThenAddReindexDetailsShouldBeCalled(
            bool enabled)
        {
            _operationsConfiguration.Reindex.Enabled = enabled;

            var provider = new OperationsCapabilityProvider(
                _operationsOptions,
                _featureOptions,
                _coreFeatureOptions,
                _implementationGuidesOptions,
                _urlResolver,
                _fhirRuntimeConfiguration);
            ICapabilityStatementBuilder builder = Substitute.For<ICapabilityStatementBuilder>();
            await provider.BuildAsync(builder, CancellationToken.None);

            builder.Received(enabled ? 1 : 0)
                .Apply(Arg.Is<Action<ListedCapabilityStatement>>(x => x.Method.Name == nameof(OperationsCapabilityProvider.AddReindexDetails)));
        }

        [Fact]
        public void GivenProvider_WhenAddingDetails_ThenReindexOperationsShouldBeAdded()
        {
            var provider = new OperationsCapabilityProvider(
                _operationsOptions,
                _featureOptions,
                _coreFeatureOptions,
                _implementationGuidesOptions,
                _urlResolver,
                _fhirRuntimeConfiguration);
            var restComponent = new ListedRestComponent()
            {
                Mode = ListedCapabilityStatement.ServerMode,
            };

            var capabilityStatement = new ListedCapabilityStatement();
            capabilityStatement.Rest.Add(restComponent);

            provider.AddReindexDetails(capabilityStatement);
            var expected = new[]
            {
                OperationsConstants.Reindex,
                OperationsConstants.ResourceReindex,
            };

            Assert.Equal(expected.Length, restComponent.Operation.Count);
            Assert.All(
                expected,
                x =>
                {
                    Assert.Contains(
                        restComponent.Operation,
                        y =>
                        {
                            return string.Equals(x, y.Name, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(OperationDefinitionUrl, y.Definition?.Reference, StringComparison.OrdinalIgnoreCase);
                        });
                });
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenConfiguration_WhenBuildingCapabilityStatement_ThenAddConvertDataDetailsShouldBeCalled(
            bool enabled)
        {
            _operationsConfiguration.ConvertData.Enabled = enabled;

            var provider = new OperationsCapabilityProvider(
                _operationsOptions,
                _featureOptions,
                _coreFeatureOptions,
                _implementationGuidesOptions,
                _urlResolver,
                _fhirRuntimeConfiguration);
            ICapabilityStatementBuilder builder = Substitute.For<ICapabilityStatementBuilder>();
            await provider.BuildAsync(builder, CancellationToken.None);

            builder.Received(enabled ? 1 : 0)
                .Apply(Arg.Is<Action<ListedCapabilityStatement>>(x => x.Method.Name == nameof(OperationsCapabilityProvider.AddConvertDataDetails)));
        }

        [Fact]
        public void GivenProvider_WhenAddingDetails_ThenConvertDataOperationShouldBeAdded()
        {
            var provider = new OperationsCapabilityProvider(
                _operationsOptions,
                _featureOptions,
                _coreFeatureOptions,
                _implementationGuidesOptions,
                _urlResolver,
                _fhirRuntimeConfiguration);
            var restComponent = new ListedRestComponent()
            {
                Mode = ListedCapabilityStatement.ServerMode,
            };

            var capabilityStatement = new ListedCapabilityStatement();
            capabilityStatement.Rest.Add(restComponent);

            provider.AddConvertDataDetails(capabilityStatement);
            Assert.Single(restComponent.Operation);
            Assert.Equal(OperationsConstants.ConvertData, restComponent.Operation.First().Name, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(OperationDefinitionUrl, restComponent.Operation.First().Definition?.Reference, StringComparer.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenConfiguration_WhenBuildingCapabilityStatement_ThenAddAnonymizedExportDetailsShouldBeCalled(
            bool enabled)
        {
            _featureConfiguration.SupportsAnonymizedExport = enabled;

            var provider = new OperationsCapabilityProvider(
                _operationsOptions,
                _featureOptions,
                _coreFeatureOptions,
                _implementationGuidesOptions,
                _urlResolver,
                _fhirRuntimeConfiguration);
            ICapabilityStatementBuilder builder = Substitute.For<ICapabilityStatementBuilder>();
            await provider.BuildAsync(builder, CancellationToken.None);

            builder.Received(enabled ? 1 : 0)
                .Apply(Arg.Is<Action<ListedCapabilityStatement>>(x => x.Method.Name == nameof(OperationsCapabilityProvider.AddAnonymizedExportDetails)));
        }

        [Fact]
        public void GivenProvider_WhenAddingDetails_ThenAnonymizedExportOperationShouldBeAdded()
        {
            var provider = new OperationsCapabilityProvider(
                _operationsOptions,
                _featureOptions,
                _coreFeatureOptions,
                _implementationGuidesOptions,
                _urlResolver,
                _fhirRuntimeConfiguration);
            var restComponent = new ListedRestComponent()
            {
                Mode = ListedCapabilityStatement.ServerMode,
            };

            var capabilityStatement = new ListedCapabilityStatement();
            capabilityStatement.Rest.Add(restComponent);

            provider.AddAnonymizedExportDetails(capabilityStatement);
            Assert.Single(restComponent.Operation);
            Assert.Equal(OperationsConstants.AnonymizedExport, restComponent.Operation.First().Name, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(OperationDefinitionUrl, restComponent.Operation.First().Definition?.Reference, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void GivenProvider_WhenAddingDetails_ThenMemberMatchOperationShouldBeAdded()
        {
            var provider = new OperationsCapabilityProvider(
                _operationsOptions,
                _featureOptions,
                _coreFeatureOptions,
                _implementationGuidesOptions,
                _urlResolver,
                _fhirRuntimeConfiguration);
            var restComponent = new ListedRestComponent()
            {
                Mode = ListedCapabilityStatement.ServerMode,
            };

            var capabilityStatement = new ListedCapabilityStatement();
            capabilityStatement.Rest.Add(restComponent);

            provider.AddMemberMatchDetails(capabilityStatement);
            Assert.Single(restComponent.Operation);
            Assert.Equal(OperationsConstants.MemberMatch, restComponent.Operation.First().Name, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(OperationDefinitionUrl, restComponent.Operation.First().Definition?.Reference, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void GivenProvider_WhenAddingDetails_ThenPatientEverythingOperationShouldBeAdded()
        {
            var restComponent = new ListedRestComponent()
            {
                Mode = ListedCapabilityStatement.ServerMode,
            };

            var capabilityStatement = new ListedCapabilityStatement();
            capabilityStatement.Rest.Add(restComponent);

            OperationsCapabilityProvider.AddPatientEverythingDetails(capabilityStatement);
            Assert.Single(restComponent.Operation);
            Assert.Equal(OperationsConstants.PatientEverything, restComponent.Operation.First().Name, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(OperationsConstants.PatientEverythingUri, restComponent.Operation.First().Definition?.Reference, StringComparer.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenConfiguration_WhenBuildingCapabilityStatement_ThenAddBulkDeleteDetailsShouldBeCalled(
            bool enabled)
        {
            _operationsConfiguration.BulkDelete.Enabled = enabled;

            var provider = new OperationsCapabilityProvider(
                _operationsOptions,
                _featureOptions,
                _coreFeatureOptions,
                _implementationGuidesOptions,
                _urlResolver,
                _fhirRuntimeConfiguration);
            ICapabilityStatementBuilder builder = Substitute.For<ICapabilityStatementBuilder>();
            await provider.BuildAsync(builder, CancellationToken.None);

            builder.Received(enabled ? 1 : 0)
                .Apply(Arg.Is<Action<ListedCapabilityStatement>>(x => x.Method.Name == nameof(OperationsCapabilityProvider.AddBulkDeleteDetails)));
        }

        [Fact]
        public void GivenProvider_WhenAddingDetails_ThenBulkDeleteOperationShouldBeAdded()
        {
            var provider = new OperationsCapabilityProvider(
                _operationsOptions,
                _featureOptions,
                _coreFeatureOptions,
                _implementationGuidesOptions,
                _urlResolver,
                _fhirRuntimeConfiguration);
            var restComponent = new ListedRestComponent()
            {
                Mode = ListedCapabilityStatement.ServerMode,
            };

            var capabilityStatement = new ListedCapabilityStatement();
            capabilityStatement.Rest.Add(restComponent);

            provider.AddBulkDeleteDetails(capabilityStatement);
            Assert.Single(restComponent.Operation);
            Assert.Equal(OperationsConstants.BulkDelete, restComponent.Operation.First().Name, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(OperationDefinitionUrl, restComponent.Operation.First().Definition?.Reference, StringComparer.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenConfiguration_WhenBuildingCapabilityStatement_ThenAddBulkUpdateDetailsShouldBeCalled(
            bool enabled)
        {
            _operationsConfiguration.BulkUpdate.Enabled = enabled;

            var provider = new OperationsCapabilityProvider(
                _operationsOptions,
                _featureOptions,
                _coreFeatureOptions,
                _implementationGuidesOptions,
                _urlResolver,
                _fhirRuntimeConfiguration);
            ICapabilityStatementBuilder builder = Substitute.For<ICapabilityStatementBuilder>();
            await provider.BuildAsync(builder, CancellationToken.None);

            builder.Received(enabled ? 1 : 0)
                .Apply(Arg.Is<Action<ListedCapabilityStatement>>(x => x.Method.Name == nameof(OperationsCapabilityProvider.AddBulkUpdateDetails)));
        }

        [Fact]
        public void GivenProvider_WhenAddingDetails_ThenBulkUpdateOperationShouldBeAdded()
        {
            var provider = new OperationsCapabilityProvider(
                _operationsOptions,
                _featureOptions,
                _coreFeatureOptions,
                _implementationGuidesOptions,
                _urlResolver,
                _fhirRuntimeConfiguration);
            var restComponent = new ListedRestComponent()
            {
                Mode = ListedCapabilityStatement.ServerMode,
            };

            var capabilityStatement = new ListedCapabilityStatement();
            capabilityStatement.Rest.Add(restComponent);

            provider.AddBulkUpdateDetails(capabilityStatement);
            Assert.Single(restComponent.Operation);
            Assert.Equal(OperationsConstants.BulkUpdate, restComponent.Operation.First().Name, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(OperationDefinitionUrl, restComponent.Operation.First().Definition?.Reference, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void GivenProvider_WhenAddingDetails_ThenSearchParameterStatusOperationShouldBeAdded()
        {
            var provider = new OperationsCapabilityProvider(
                _operationsOptions,
                _featureOptions,
                _coreFeatureOptions,
                _implementationGuidesOptions,
                _urlResolver,
                _fhirRuntimeConfiguration);
            var restComponent = new ListedRestComponent()
            {
                Mode = ListedCapabilityStatement.ServerMode,
            };

            var capabilityStatement = new ListedCapabilityStatement();
            capabilityStatement.Rest.Add(restComponent);

            provider.AddSelectableSearchParameterDetails(capabilityStatement);
            Assert.Single(restComponent.Operation);
            Assert.Equal(OperationsConstants.SearchParameterStatus, restComponent.Operation.First().Name, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(OperationDefinitionUrl, restComponent.Operation.First().Definition?.Reference, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void GivenProvider_WhenAddingDetails_ThenIncludesOperationShouldBeAdded()
        {
            var provider = new OperationsCapabilityProvider(
                _operationsOptions,
                _featureOptions,
                _coreFeatureOptions,
                _implementationGuidesOptions,
                _urlResolver,
                _fhirRuntimeConfiguration);
            var restComponent = new ListedRestComponent()
            {
                Mode = ListedCapabilityStatement.ServerMode,
            };

            var capabilityStatement = new ListedCapabilityStatement();
            capabilityStatement.Rest.Add(restComponent);

            provider.AddIncludesDetails(capabilityStatement);
            Assert.Single(restComponent.Operation);
            Assert.Equal(OperationsConstants.Includes, restComponent.Operation.First().Name, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(OperationDefinitionUrl, restComponent.Operation.First().Definition?.Reference, StringComparer.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenConfiguration_WhenBuildingCapabilityStatement_ThenAddDocRefDetailsShouldBeCalled(
            bool enabled)
        {
            _implementationGuidesConfiguration.USCore.EnableDocRef = enabled;

            var provider = new OperationsCapabilityProvider(
                _operationsOptions,
                _featureOptions,
                _coreFeatureOptions,
                _implementationGuidesOptions,
                _urlResolver,
                _fhirRuntimeConfiguration);
            ICapabilityStatementBuilder builder = Substitute.For<ICapabilityStatementBuilder>();
            await provider.BuildAsync(builder, CancellationToken.None);

            builder.Received(enabled ? 1 : 0)
                .Apply(Arg.Is<Action<ListedCapabilityStatement>>(x => x.Method.Name == nameof(OperationsCapabilityProvider.AddDocRefDetails)));
        }

        [Fact]
        public void GivenProvider_WhenAddingDetails_ThenDocRefOperationShouldBeAdded()
        {
            var provider = new OperationsCapabilityProvider(
                _operationsOptions,
                _featureOptions,
                _coreFeatureOptions,
                _implementationGuidesOptions,
                _urlResolver,
                _fhirRuntimeConfiguration);
            var restComponent = new ListedRestComponent()
            {
                Mode = ListedCapabilityStatement.ServerMode,
            };

            var capabilityStatement = new ListedCapabilityStatement();
            capabilityStatement.Rest.Add(restComponent);

            provider.AddDocRefDetails(capabilityStatement);
            Assert.Single(restComponent.Operation);
            Assert.Equal(OperationsConstants.DocRef, restComponent.Operation.First().Name, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(OperationDefinitionUrl, restComponent.Operation.First().Definition?.Reference, StringComparer.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenConfiguration_WhenBuildingCapabilityStatement_ThenAddExpandDetailsShouldBeCalled(
            bool enabled)
        {
            _operationsConfiguration.Terminology.EnableExpand = enabled;

            var provider = new OperationsCapabilityProvider(
                _operationsOptions,
                _featureOptions,
                _coreFeatureOptions,
                _implementationGuidesOptions,
                _urlResolver,
                _fhirRuntimeConfiguration);
            ICapabilityStatementBuilder builder = Substitute.For<ICapabilityStatementBuilder>();
            await provider.BuildAsync(builder, CancellationToken.None);

            builder.Received(enabled ? 1 : 0)
                .Apply(Arg.Is<Action<ListedCapabilityStatement>>(x => x.Method.Name == nameof(OperationsCapabilityProvider.AddExpandDetails)));
        }

        [Fact]
        public void GivenProvider_WhenAddingDetails_ThenExpandOperationShouldBeAdded()
        {
            var provider = new OperationsCapabilityProvider(
                _operationsOptions,
                _featureOptions,
                _coreFeatureOptions,
                _implementationGuidesOptions,
                _urlResolver,
                _fhirRuntimeConfiguration);
            var restComponent = new ListedRestComponent()
            {
                Mode = ListedCapabilityStatement.ServerMode,
            };

            var capabilityStatement = new ListedCapabilityStatement();
            capabilityStatement.Rest.Add(restComponent);

            provider.AddExpandDetails(capabilityStatement);
            Assert.Single(restComponent.Operation);
            Assert.Equal(OperationsConstants.ValueSetExpand, restComponent.Operation.First().Name, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(OperationDefinitionUrl, restComponent.Operation.First().Definition?.Reference, StringComparer.OrdinalIgnoreCase);
        }
    }
}
