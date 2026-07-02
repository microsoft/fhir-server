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
using Microsoft.Health.Fhir.SqlServer.Registration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
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
        private readonly IQueryPlanReuseChecker _queryPlanReuseChecker;
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
            _queryPlanReuseChecker = Substitute.For<IQueryPlanReuseChecker>();

            var config = new SqlServerDataStoreConfiguration
            {
                CommandTimeout = TimeSpan.FromSeconds(30),
            };

            var fhirConfig = new FhirSqlServerConfiguration();

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
                fhirConfig,
                _schemaInformation,
                _requestContextAccessor,
                _compressedRawResourceConverter,
                _queryHashCalculator,
                _queryPlanReuseChecker,
                Array.Empty<ISearchParameterQueryParameterExpander>(),
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
                    new FhirSqlServerConfiguration(),
                    schemaInfo,
                    _requestContextAccessor,
                    _compressedRawResourceConverter,
                    _queryHashCalculator,
                    _queryPlanReuseChecker,
                    Array.Empty<ISearchParameterQueryParameterExpander>(),
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
                    new FhirSqlServerConfiguration(),
                    schemaInfo,
                    _requestContextAccessor,
                    _compressedRawResourceConverter,
                    _queryHashCalculator,
                    _queryPlanReuseChecker,
                    Array.Empty<ISearchParameterQueryParameterExpander>(),
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
                    new FhirSqlServerConfiguration(),
                    null,
                    _requestContextAccessor,
                    _compressedRawResourceConverter,
                    _queryHashCalculator,
                    _queryPlanReuseChecker,
                    Array.Empty<ISearchParameterQueryParameterExpander>(),
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

        public static IEnumerable<object[]> SingleColumnTableData()
        {
            yield return new object[] { VLatest.TokenSearchParam.TableName, VLatest.TokenSearchParam.Code.Metadata.Name };
            yield return new object[] { VLatest.StringSearchParam.TableName, VLatest.StringSearchParam.Text.Metadata.Name };
            yield return new object[] { VLatest.ReferenceSearchParam.TableName, VLatest.ReferenceSearchParam.ReferenceResourceId.Metadata.Name };
        }

        [Theory]
        [MemberData(nameof(SingleColumnTableData))]
        public void GetKeyColumns_ForSingleColumnTable_ReturnsOnlyExpectedColumn(string tableName, string expectedColumn)
        {
            // Act
            var columns = SqlServerSearchService.GetKeyColumns(tableName);

            // Assert
            Assert.Contains(expectedColumn, columns);
            Assert.Single(columns);
        }

        public static IEnumerable<object[]> TwoColumnTableData()
        {
            yield return new object[] { VLatest.DateTimeSearchParam.TableName, VLatest.DateTimeSearchParam.StartDateTime.Metadata.Name, VLatest.DateTimeSearchParam.EndDateTime.Metadata.Name };
            yield return new object[] { VLatest.NumberSearchParam.TableName, VLatest.NumberSearchParam.LowValue.Metadata.Name, VLatest.NumberSearchParam.HighValue.Metadata.Name };
            yield return new object[] { VLatest.QuantitySearchParam.TableName, VLatest.QuantitySearchParam.LowValue.Metadata.Name, VLatest.QuantitySearchParam.HighValue.Metadata.Name };
        }

        [Theory]
        [MemberData(nameof(TwoColumnTableData))]
        public void GetKeyColumns_ForTwoColumnTable_ReturnsBothExpectedColumns(string tableName, string column1, string column2)
        {
            // Act
            var columns = SqlServerSearchService.GetKeyColumns(tableName);

            // Assert
            Assert.Contains(column1, columns);
            Assert.Contains(column2, columns);
            Assert.Equal(2, columns.Count);
        }

        [Theory]
        [InlineData("dbo.UnknownTable")] // NotExists expressions resolve to no known table — stats must be skipped
        [InlineData(null)]
        public void GetKeyColumns_ForUnknownOrNullTable_ReturnsEmptySet(string tableName)
        {
            // Act
            var columns = SqlServerSearchService.GetKeyColumns(tableName);

            // Assert
            Assert.Empty(columns);
        }

        [Fact]
        public void CollectNotExistsLeaves_WithResourceSurrogateId_DetectsSurrogateIdAndMissingParam()
        {
            // Arrange: NotExists predicate with MissingSearchParameterExpression + ResourceSurrogateId constraint
            var genderParam = new SearchParameterInfo("gender", "gender");
            var missingExpr = Expression.MissingSearchParameter(genderParam, false);
            var surrogateIdExpr = Expression.SearchParameter(
                SqlSearchParameters.ResourceSurrogateIdParameter,
                Expression.GreaterThanOrEqual(SqlFieldName.ResourceSurrogateId, null, 100L));

            var predicate = Expression.And(missingExpr, surrogateIdExpr);

            var missingParams = new List<MissingSearchParameterExpression>();
            var resourceTypeIds = new HashSet<short>();
            bool foundSurrogateId = false;

            // Act
            SqlServerSearchService.ResourceSearchParamStats.CollectNotExistsLeaves(
                predicate, missingParams, resourceTypeIds, null, ref foundSurrogateId);

            // Assert
            Assert.Single(missingParams);
            Assert.Equal("gender", missingParams[0].Parameter.Name);
            Assert.True(foundSurrogateId, "Should detect ResourceSurrogateId constraint");
        }

        [Fact]
        public void CollectNotExistsLeaves_WithoutResourceSurrogateId_DoesNotSetSurrogateIdFlag()
        {
            // Arrange: NotExists predicate with only MissingSearchParameterExpression
            var genderParam = new SearchParameterInfo("gender", "gender");
            var missingExpr = Expression.MissingSearchParameter(genderParam, false);

            var missingParams = new List<MissingSearchParameterExpression>();
            var resourceTypeIds = new HashSet<short>();
            bool foundSurrogateId = false;

            // Act
            SqlServerSearchService.ResourceSearchParamStats.CollectNotExistsLeaves(
                missingExpr, missingParams, resourceTypeIds, null, ref foundSurrogateId);

            // Assert
            Assert.Single(missingParams);
            Assert.Equal("gender", missingParams[0].Parameter.Name);
            Assert.False(foundSurrogateId, "Should not detect ResourceSurrogateId when absent");
        }

        [Fact]
        public void CollectNotExistsLeaves_AmbiguousSameTablePredicates_CollectsMultipleMissingParams()
        {
            // Arrange: Two MissingSearchParameterExpression nodes in the same predicate (ambiguous)
            var genderParam = new SearchParameterInfo("gender", "gender");
            var codeParam = new SearchParameterInfo("code", "code");
            var missing1 = Expression.MissingSearchParameter(genderParam, false);
            var missing2 = Expression.MissingSearchParameter(codeParam, false);

            var predicate = Expression.And(missing1, missing2);

            var missingParams = new List<MissingSearchParameterExpression>();
            var resourceTypeIds = new HashSet<short>();
            bool foundSurrogateId = false;

            // Act
            SqlServerSearchService.ResourceSearchParamStats.CollectNotExistsLeaves(
                predicate, missingParams, resourceTypeIds, null, ref foundSurrogateId);

            // Assert: Two missing params collected — ProcessNotExistsForStats would skip this (ambiguous)
            Assert.Equal(2, missingParams.Count);
            Assert.False(foundSurrogateId);
        }

        [Fact]
        public void CollectNotExistsLeaves_MixedPredicates_CollectsAllLeafTypes()
        {
            // Arrange: MissingSearchParameterExpression + unrelated param + ResourceSurrogateId
            var genderParam = new SearchParameterInfo("gender", "gender");
            var missingExpr = Expression.MissingSearchParameter(genderParam, false);

            var otherParam = new SearchParameterInfo("active", "active");
            var otherExpr = Expression.SearchParameter(
                otherParam,
                Expression.StringEquals(FieldName.TokenCode, null, "true", false));

            var surrogateIdExpr = Expression.SearchParameter(
                SqlSearchParameters.ResourceSurrogateIdParameter,
                Expression.GreaterThanOrEqual(SqlFieldName.ResourceSurrogateId, null, 100L));

            var predicate = Expression.And(missingExpr, otherExpr, surrogateIdExpr);

            var missingParams = new List<MissingSearchParameterExpression>();
            var resourceTypeIds = new HashSet<short>();
            bool foundSurrogateId = false;

            // Act
            SqlServerSearchService.ResourceSearchParamStats.CollectNotExistsLeaves(
                predicate, missingParams, resourceTypeIds, null, ref foundSurrogateId);

            // Assert
            Assert.Single(missingParams);
            Assert.Equal("gender", missingParams[0].Parameter.Name);
            Assert.True(foundSurrogateId);
        }

        [Fact]
        public void ApplyDateEqualitySemantics_WhenContainmentEnabledAndScalarOff_PassesExpressionThroughUnchanged()
        {
            SearchParameterExpression containment = BuildExactDayBirthdateEquality();

            Expression result = SqlServerSearchService.ApplyDateEqualitySemantics(containment, enableFhirDateContainment: true, enableScalarTemporalRewriter: false);

            // Behavior unique to this combination: no rewriter runs, so Core's containment range reaches SQL
            // by reference. The no-overlap / no-union semantics are already covered by the flag matrix below.
            Assert.Same(containment, result);
        }

        [Fact]
        public void ApplyDateEqualitySemantics_WhenExpressionIsNull_ReturnsNull()
        {
            // The null guard short-circuits before either flag is read, so one combination proves the contract
            // the caller relies on: a filterless search passes its null expression straight through.
            Assert.Null(SqlServerSearchService.ApplyDateEqualitySemantics(null, enableFhirDateContainment: true, enableScalarTemporalRewriter: true));
        }

        // Full date-equality flag matrix for an EXACT-DAY birthdate query, exercising the single mutually-exclusive
        // dispatch (ApplyDateEqualitySemantics) exactly as CreateDefaultSearchExpression wires it. The scalar-temporal
        // optimization and the containment range form are never layered: each flag combination resolves to exactly one
        // path. VP2: a partial-precision stored birthdate (e.g. year-only '1990', stored as a multi-day range with
        // IsLongerThanADay = true and DateTimeEnd = 1990-12-31T23:59) MATCHES under the overlap form
        // (DateTimeStart <= hi AND DateTimeEnd >= lo) but is DROPPED under containment. The overlap predicate is
        // present iff legacy overlap is applied; the IsLongerThanADay=false collapse appears only when the
        // rewriter runs. No path may emit a temporal UNION.
        [Theory]
        [InlineData(true, true, true, false)] // both ON => End-only collapse (containment); year-only dropped
        [InlineData(false, true, false, false)] // containment ON, rewriter OFF => containment two-predicate; year-only dropped
        [InlineData(true, false, false, true)] // containment OFF => legacy overlap; year-only matched
        [InlineData(false, false, false, true)] // both OFF => legacy overlap; year-only matched
        public void DateEqualityPipeline_AcrossFlagMatrix_AppliesExpectedSemanticsAndNeverUnions(
            bool enableScalarTemporalRewriter,
            bool enableFhirDateContainment,
            bool expectEndOnlyCollapse,
            bool expectLegacyOverlap)
        {
            SearchParameterExpression query = BuildExactDayBirthdateEquality();

            Expression result = SqlServerSearchService.ApplyDateEqualitySemantics(
                query, enableFhirDateContainment, enableScalarTemporalRewriter);

            bool hasEndOnlyCollapse = ContainsPredicate(
                result,
                e => e is BinaryExpression { FieldName: SqlFieldName.DateTimeIsLongerThanADay, BinaryOperator: BinaryOperator.Equal, Value: false });
            bool hasOverlapPredicate = ContainsPredicate(
                result,
                e => e is BinaryExpression { FieldName: FieldName.DateTimeStart, BinaryOperator: BinaryOperator.LessThanOrEqual });

            Assert.Equal(expectEndOnlyCollapse, hasEndOnlyCollapse);
            Assert.Equal(expectLegacyOverlap, hasOverlapPredicate);
            Assert.False(ContainsPredicate(result, e => e is UnionExpression), "No date-equality flag combination may emit a temporal UNION.");
        }

        private static SearchParameterExpression BuildExactDayBirthdateEquality()
        {
            var dateParam = new SearchParameterInfo(
                "birthdate",
                "birthdate",
                SearchParamType.Date,
                new Uri("http://hl7.org/fhir/SearchParameter/individual-birthdate"),
                expression: "Patient.birthDate",
                baseResourceTypes: new[] { "Patient" });

            // Exact UTC calendar day (end-of-day to the tick) so the scalar-temporal rewriter collapses it.
            var lo = new DateTimeOffset(1990, 5, 15, 0, 0, 0, TimeSpan.Zero);
            var hi = new DateTimeOffset(1990, 5, 15, 23, 59, 59, TimeSpan.Zero).AddTicks(9999999);

            return Expression.SearchParameter(
                dateParam,
                Expression.And(
                    Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, lo),
                    Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, hi)));
        }

        private static bool ContainsPredicate(Expression expression, Func<Expression, bool> predicate)
        {
            if (expression == null)
            {
                return false;
            }

            if (predicate(expression))
            {
                return true;
            }

            switch (expression)
            {
                case SearchParameterExpression searchParameter:
                    return ContainsPredicate(searchParameter.Expression, predicate);
                case MultiaryExpression multiary:
                    return multiary.Expressions.Any(e => ContainsPredicate(e, predicate));
                case UnionExpression union:
                    return union.Expressions.Any(e => ContainsPredicate(e, predicate));
                default:
                    return false;
            }
        }
    }
}
