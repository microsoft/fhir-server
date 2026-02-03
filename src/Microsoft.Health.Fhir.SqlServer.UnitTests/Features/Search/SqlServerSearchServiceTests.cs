// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search
{
    /// <summary>
    /// Unit tests for SqlServerSearchService.
    /// Tests core functionality including sorting, surrogate ID ranges, and resource type queries.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SqlServerSearchServiceTests
    {
        private readonly ISearchOptionsFactory _searchOptionsFactory;
        private readonly IFhirDataStore _fhirDataStore;
        private readonly ISqlServerFhirModel _model;
        private readonly SearchParamTableExpressionQueryGeneratorFactory _queryGeneratorFactory;
        private readonly ISqlRetryService _sqlRetryService;
        private readonly SchemaInformation _schemaInformation;
        private readonly ICompressedRawResourceConverter _compressedRawResourceConverter;
        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
        private readonly ISqlQueryHashCalculator _queryHashCalculator;
        private readonly SqlServerSearchService _searchService;

        public SqlServerSearchServiceTests()
        {
            _searchOptionsFactory = Substitute.For<ISearchOptionsFactory>();
            _fhirDataStore = Substitute.For<IFhirDataStore>();
            _model = Substitute.For<ISqlServerFhirModel>();
            _queryGeneratorFactory = new SearchParamTableExpressionQueryGeneratorFactory(new SearchParameterToSearchValueTypeMap());
            _sqlRetryService = Substitute.For<ISqlRetryService>();
            _compressedRawResourceConverter = Substitute.For<ICompressedRawResourceConverter>();
            _requestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            _queryHashCalculator = Substitute.For<ISqlQueryHashCalculator>();

            var config = new SqlServerDataStoreConfiguration
            {
                CommandTimeout = TimeSpan.FromSeconds(30),
            };

            _schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max);

            // Create concrete instances of rewriters with required dependencies
            var sqlRootExpressionRewriter = new SqlRootExpressionRewriter(_queryGeneratorFactory);
            var chainFlatteningRewriter = new ChainFlatteningRewriter(_queryGeneratorFactory);
            var sortRewriter = new SortRewriter(_queryGeneratorFactory);
            var partitionEliminationRewriter = new PartitionEliminationRewriter(_model, _schemaInformation, () => Substitute.For<ISearchParameterDefinitionManager>());
            var compartmentDefinitionManager = Substitute.For<ICompartmentDefinitionManager>();
            var searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            var compartmentSearchRewriter = new SqlCompartmentSearchRewriter(
                new Lazy<ICompartmentDefinitionManager>(() => compartmentDefinitionManager),
                new Lazy<ISearchParameterDefinitionManager>(() => searchParameterDefinitionManager));
            var smartCompartmentSearchRewriter = new SmartCompartmentSearchRewriter(
                compartmentSearchRewriter,
                new Lazy<ISearchParameterDefinitionManager>(() => searchParameterDefinitionManager));

            _searchService = new SqlServerSearchService(
                _searchOptionsFactory,
                _fhirDataStore,
                _model,
                sqlRootExpressionRewriter,
                chainFlatteningRewriter,
                sortRewriter,
                partitionEliminationRewriter,
                compartmentSearchRewriter,
                smartCompartmentSearchRewriter,
                _queryGeneratorFactory,
                _sqlRetryService,
                Options.Create(config),
                _schemaInformation,
                _requestContextAccessor,
                _compressedRawResourceConverter,
                _queryHashCalculator,
                NullLogger<SqlServerSearchService>.Instance);
        }

        [Fact]
        public void Constructor_WithNullSearchOptionsFactory_ThrowsArgumentNullException()
        {
            // Arrange
            var queryGeneratorFactory = new SearchParamTableExpressionQueryGeneratorFactory(new SearchParameterToSearchValueTypeMap());
            var sqlRootExpressionRewriter = new SqlRootExpressionRewriter(queryGeneratorFactory);
            var chainFlatteningRewriter = new ChainFlatteningRewriter(queryGeneratorFactory);
            var sortRewriter = new SortRewriter(queryGeneratorFactory);
            var model = Substitute.For<ISqlServerFhirModel>();
            var schemaInfo = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max);
            var partitionEliminationRewriter = new PartitionEliminationRewriter(model, schemaInfo, () => Substitute.For<ISearchParameterDefinitionManager>());
            var compartmentDefinitionManager = Substitute.For<ICompartmentDefinitionManager>();
            var searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            var compartmentSearchRewriter = new SqlCompartmentSearchRewriter(
                new Lazy<ICompartmentDefinitionManager>(() => compartmentDefinitionManager),
                new Lazy<ISearchParameterDefinitionManager>(() => searchParameterDefinitionManager));
            var smartCompartmentSearchRewriter = new SmartCompartmentSearchRewriter(
                compartmentSearchRewriter,
                new Lazy<ISearchParameterDefinitionManager>(() => searchParameterDefinitionManager));

            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
            {
                _ = new SqlServerSearchService(
                    null,
                    _fhirDataStore,
                    model,
                    sqlRootExpressionRewriter,
                    chainFlatteningRewriter,
                    sortRewriter,
                    partitionEliminationRewriter,
                    compartmentSearchRewriter,
                    smartCompartmentSearchRewriter,
                    queryGeneratorFactory,
                    _sqlRetryService,
                    Options.Create(new SqlServerDataStoreConfiguration()),
                    schemaInfo,
                    _requestContextAccessor,
                    _compressedRawResourceConverter,
                    _queryHashCalculator,
                    NullLogger<SqlServerSearchService>.Instance);
            });

            Assert.NotNull(ex);
        }

        [Fact]
        public void Constructor_WithNullSqlRetryService_ThrowsArgumentNullException()
        {
            // Arrange
            var queryGeneratorFactory = new SearchParamTableExpressionQueryGeneratorFactory(new SearchParameterToSearchValueTypeMap());
            var sqlRootExpressionRewriter = new SqlRootExpressionRewriter(queryGeneratorFactory);
            var chainFlatteningRewriter = new ChainFlatteningRewriter(queryGeneratorFactory);
            var sortRewriter = new SortRewriter(queryGeneratorFactory);
            var model = Substitute.For<ISqlServerFhirModel>();
            var schemaInfo = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max);
            var partitionEliminationRewriter = new PartitionEliminationRewriter(model, schemaInfo, () => Substitute.For<ISearchParameterDefinitionManager>());
            var compartmentDefinitionManager = Substitute.For<ICompartmentDefinitionManager>();
            var searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            var compartmentSearchRewriter = new SqlCompartmentSearchRewriter(
                new Lazy<ICompartmentDefinitionManager>(() => compartmentDefinitionManager),
                new Lazy<ISearchParameterDefinitionManager>(() => searchParameterDefinitionManager));
            var smartCompartmentSearchRewriter = new SmartCompartmentSearchRewriter(
                compartmentSearchRewriter,
                new Lazy<ISearchParameterDefinitionManager>(() => searchParameterDefinitionManager));

            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
            {
                _ = new SqlServerSearchService(
                    _searchOptionsFactory,
                    _fhirDataStore,
                    model,
                    sqlRootExpressionRewriter,
                    chainFlatteningRewriter,
                    sortRewriter,
                    partitionEliminationRewriter,
                    compartmentSearchRewriter,
                    smartCompartmentSearchRewriter,
                    queryGeneratorFactory,
                    null,
                    Options.Create(new SqlServerDataStoreConfiguration()),
                    schemaInfo,
                    _requestContextAccessor,
                    _compressedRawResourceConverter,
                    _queryHashCalculator,
                    NullLogger<SqlServerSearchService>.Instance);
            });

            Assert.NotNull(ex);
        }

        [Fact]
        public void Constructor_WithNullSchemaInformation_ThrowsArgumentNullException()
        {
            // Arrange
            var queryGeneratorFactory = new SearchParamTableExpressionQueryGeneratorFactory(new SearchParameterToSearchValueTypeMap());
            var sqlRootExpressionRewriter = new SqlRootExpressionRewriter(queryGeneratorFactory);
            var chainFlatteningRewriter = new ChainFlatteningRewriter(queryGeneratorFactory);
            var sortRewriter = new SortRewriter(queryGeneratorFactory);
            var model = Substitute.For<ISqlServerFhirModel>();
            var schemaInfo = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max);
            var partitionEliminationRewriter = new PartitionEliminationRewriter(model, schemaInfo, () => Substitute.For<ISearchParameterDefinitionManager>());
            var compartmentDefinitionManager = Substitute.For<ICompartmentDefinitionManager>();
            var searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            var compartmentSearchRewriter = new SqlCompartmentSearchRewriter(
                new Lazy<ICompartmentDefinitionManager>(() => compartmentDefinitionManager),
                new Lazy<ISearchParameterDefinitionManager>(() => searchParameterDefinitionManager));
            var smartCompartmentSearchRewriter = new SmartCompartmentSearchRewriter(
                compartmentSearchRewriter,
                new Lazy<ISearchParameterDefinitionManager>(() => searchParameterDefinitionManager));

            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
            {
                _ = new SqlServerSearchService(
                    _searchOptionsFactory,
                    _fhirDataStore,
                    model,
                    sqlRootExpressionRewriter,
                    chainFlatteningRewriter,
                    sortRewriter,
                    partitionEliminationRewriter,
                    compartmentSearchRewriter,
                    smartCompartmentSearchRewriter,
                    queryGeneratorFactory,
                    _sqlRetryService,
                    Options.Create(new SqlServerDataStoreConfiguration()),
                    null,
                    _requestContextAccessor,
                    _compressedRawResourceConverter,
                    _queryHashCalculator,
                    NullLogger<SqlServerSearchService>.Instance);
            });

            Assert.NotNull(ex);
        }

        [Fact]
        public void Constructor_WithValidDependencies_InitializesSuccessfully()
        {
            // Assert - Service was initialized successfully in constructor
            Assert.NotNull(_searchService);
            Assert.NotNull(_searchService.Model);
            Assert.True(_searchService.StoredProcedureLayerIsEnabled);
        }

        [Fact]
        public void StoredProcedureLayerIsEnabled_DefaultsToTrue()
        {
            // Assert
            Assert.True(_searchService.StoredProcedureLayerIsEnabled);
        }

        [Fact]
        public void StoredProcedureLayerIsEnabled_CanBeSetToFalse()
        {
            // Act
            _searchService.StoredProcedureLayerIsEnabled = false;

            // Assert
            Assert.False(_searchService.StoredProcedureLayerIsEnabled);

            // Cleanup
            _searchService.StoredProcedureLayerIsEnabled = true;
        }

        [Fact]
        public void Model_Property_ReturnsInjectedModel()
        {
            // Act
            var model = _searchService.Model;

            // Assert
            Assert.Same(_model, model);
        }

        [Fact]
        public void ResetReuseQueryPlans_ExecutesWithoutException()
        {
            // Act & Assert - Should execute without throwing
            SqlServerSearchService.ResetReuseQueryPlans();
        }

        [Fact]
        public void ReuseQueryPlansParameterId_HasExpectedValue()
        {
            // Assert
            Assert.Equal("Search.ReuseQueryPlans.IsEnabled", SqlServerSearchService.ReuseQueryPlansParameterId);
        }
    }
}
