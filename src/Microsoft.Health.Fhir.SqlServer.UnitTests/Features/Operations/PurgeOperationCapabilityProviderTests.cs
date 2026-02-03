// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.SqlServer.Features.Operations;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Operations
{
    /// <summary>
    /// Unit tests for PurgeOperationCapabilityProvider.
    /// Tests the conditional logic for adding purge operation to capability statement.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class PurgeOperationCapabilityProviderTests
    {
        private const string PurgeOperationDefinitionUrl = "http://test.com/OperationDefinition/purge";

        private readonly SchemaInformation _schemaInformation;
        private readonly IUrlResolver _urlResolver;
        private readonly PurgeOperationCapabilityProvider _provider;

        public PurgeOperationCapabilityProviderTests()
        {
            _schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max);
            _urlResolver = Substitute.For<IUrlResolver>();
            _urlResolver.ResolveOperationDefinitionUrl(OperationsConstants.PurgeHistory)
                .Returns(new Uri(PurgeOperationDefinitionUrl));

            _provider = new PurgeOperationCapabilityProvider(_schemaInformation, _urlResolver);
        }

        [Fact]
        public void GivenNullSchemaInformation_WhenConstructing_ThenThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new PurgeOperationCapabilityProvider(null, _urlResolver));
        }

        [Fact]
        public void GivenNullUrlResolver_WhenConstructing_ThenThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new PurgeOperationCapabilityProvider(_schemaInformation, null));
        }

        [Fact]
        public async Task GivenSchemaVersionBelowPurgeHistoryVersion_WhenBuildingCapabilityStatement_ThenPurgeOperationNotAdded()
        {
            // Arrange - Set current version below PurgeHistoryVersion
            var schemaInfo = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max)
            {
                Current = SchemaVersionConstants.PurgeHistoryVersion - 1,
            };

            var provider = new PurgeOperationCapabilityProvider(schemaInfo, _urlResolver);
            var capabilityStatement = CreateCapabilityStatement();
            var builder = CreateCapabilityStatementBuilder(capabilityStatement);

            // Act
            await provider.BuildAsync(builder, CancellationToken.None);

            // Assert
            var restComponent = capabilityStatement.Rest.Server();
            Assert.Empty(restComponent.Operation);
            _urlResolver.DidNotReceive().ResolveOperationDefinitionUrl(Arg.Any<string>());
        }

        [Fact]
        public async Task GivenSchemaVersionEqualsToPurgeHistoryVersion_WhenBuildingCapabilityStatement_ThenPurgeOperationAdded()
        {
            // Arrange - Set current version equal to PurgeHistoryVersion
            var schemaInfo = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max)
            {
                Current = SchemaVersionConstants.PurgeHistoryVersion,
            };

            var provider = new PurgeOperationCapabilityProvider(schemaInfo, _urlResolver);
            var capabilityStatement = CreateCapabilityStatement();
            var builder = CreateCapabilityStatementBuilder(capabilityStatement);

            // Act
            await provider.BuildAsync(builder, CancellationToken.None);

            // Assert
            var restComponent = capabilityStatement.Rest.Server();
            Assert.Single(restComponent.Operation);
            var operation = restComponent.Operation.First();
            Assert.Equal(OperationsConstants.PurgeHistory, operation.Name);
            Assert.Equal(PurgeOperationDefinitionUrl, operation.Definition?.Reference);
            _urlResolver.Received(1).ResolveOperationDefinitionUrl(OperationsConstants.PurgeHistory);
        }

        [Fact]
        public async Task GivenSchemaVersionAbovePurgeHistoryVersion_WhenBuildingCapabilityStatement_ThenPurgeOperationAdded()
        {
            // Arrange - Set current version above PurgeHistoryVersion
            var schemaInfo = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max)
            {
                Current = SchemaVersionConstants.PurgeHistoryVersion + 10,
            };

            var provider = new PurgeOperationCapabilityProvider(schemaInfo, _urlResolver);
            var capabilityStatement = CreateCapabilityStatement();
            var builder = CreateCapabilityStatementBuilder(capabilityStatement);

            // Act
            await provider.BuildAsync(builder, CancellationToken.None);

            // Assert
            var restComponent = capabilityStatement.Rest.Server();
            Assert.Single(restComponent.Operation);
            var operation = restComponent.Operation.First();
            Assert.Equal(OperationsConstants.PurgeHistory, operation.Name);
            Assert.Equal(PurgeOperationDefinitionUrl, operation.Definition?.Reference);
        }

        [Fact]
        public async Task GivenNullCurrentSchemaVersion_WhenBuildingCapabilityStatement_ThenPurgeOperationNotAdded()
        {
            // Arrange - Current version is null
            var schemaInfo = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max)
            {
                Current = null,
            };

            var provider = new PurgeOperationCapabilityProvider(schemaInfo, _urlResolver);
            var capabilityStatement = CreateCapabilityStatement();
            var builder = CreateCapabilityStatementBuilder(capabilityStatement);

            // Act
            await provider.BuildAsync(builder, CancellationToken.None);

            // Assert
            var restComponent = capabilityStatement.Rest.Server();
            Assert.Empty(restComponent.Operation);
        }

        [Fact]
        public async Task GivenValidConfiguration_WhenBuildingCapabilityStatement_ThenBuilderApplyIsCalledOnce()
        {
            // Arrange
            var schemaInfo = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max)
            {
                Current = SchemaVersionConstants.PurgeHistoryVersion,
            };

            var provider = new PurgeOperationCapabilityProvider(schemaInfo, _urlResolver);
            var builder = Substitute.For<ICapabilityStatementBuilder>();

            // Act
            await provider.BuildAsync(builder, CancellationToken.None);

            // Assert
            builder.Received(1).Apply(Arg.Any<Action<ListedCapabilityStatement>>());
        }

        [Fact]
        public async Task GivenSchemaVersionBelowThreshold_WhenBuildingCapabilityStatement_ThenBuilderApplyNotCalled()
        {
            // Arrange
            var schemaInfo = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max)
            {
                Current = SchemaVersionConstants.PurgeHistoryVersion - 1,
            };

            var provider = new PurgeOperationCapabilityProvider(schemaInfo, _urlResolver);
            var builder = Substitute.For<ICapabilityStatementBuilder>();

            // Act
            await provider.BuildAsync(builder, CancellationToken.None);

            // Assert
            builder.DidNotReceive().Apply(Arg.Any<Action<ListedCapabilityStatement>>());
        }

        [Theory]
        [InlineData(SchemaVersionConstants.PurgeHistoryVersion - 1, false)]
        [InlineData(SchemaVersionConstants.PurgeHistoryVersion, true)]
        [InlineData(SchemaVersionConstants.PurgeHistoryVersion + 1, true)]
        [InlineData(SchemaVersionConstants.Max, true)]
        public async Task GivenVariousSchemaVersions_WhenBuildingCapabilityStatement_ThenPurgeOperationAddedCorrectly(
            int schemaVersion,
            bool shouldAddOperation)
        {
            // Arrange
            var schemaInfo = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max)
            {
                Current = schemaVersion,
            };

            var provider = new PurgeOperationCapabilityProvider(schemaInfo, _urlResolver);
            var capabilityStatement = CreateCapabilityStatement();
            var builder = CreateCapabilityStatementBuilder(capabilityStatement);

            // Act
            await provider.BuildAsync(builder, CancellationToken.None);

            // Assert
            var restComponent = capabilityStatement.Rest.Server();
            if (shouldAddOperation)
            {
                Assert.Single(restComponent.Operation);
                Assert.Equal(OperationsConstants.PurgeHistory, restComponent.Operation.First().Name);
            }
            else
            {
                Assert.Empty(restComponent.Operation);
            }
        }

        private static ListedCapabilityStatement CreateCapabilityStatement()
        {
            var restComponent = new ListedRestComponent
            {
                Mode = ListedCapabilityStatement.ServerMode,
            };

            var capabilityStatement = new ListedCapabilityStatement();
            capabilityStatement.Rest.Add(restComponent);

            return capabilityStatement;
        }

        private static ICapabilityStatementBuilder CreateCapabilityStatementBuilder(ListedCapabilityStatement capabilityStatement)
        {
            var builder = Substitute.For<ICapabilityStatementBuilder>();
            builder.When(x => x.Apply(Arg.Any<Action<ListedCapabilityStatement>>()))
                .Do(x =>
                {
                    var action = x.Arg<Action<ListedCapabilityStatement>>();
                    action.Invoke(capabilityStatement);
                });

            return builder;
        }
    }
}
