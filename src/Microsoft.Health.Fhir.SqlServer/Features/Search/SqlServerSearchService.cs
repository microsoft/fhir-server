// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Rest;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Registration;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Schema.Model;
using SortOrder = Microsoft.Health.Fhir.Core.Features.Search.SortOrder;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    /// <summary>
    /// SQL Server search service implementation.
    /// </summary>
    internal partial class SqlServerSearchService : SearchService
    {
        internal const string ReuseQueryPlansParameterId = "Search.ReuseQueryPlans.IsEnabled";

        private const string SortValueColumnName = "SortValue";

        private readonly ISqlServerFhirModel _model;
        private readonly CompartmentSearchRewriter _compartmentSearchRewriter;
        private readonly SmartCompartmentSearchRewriter _smartCompartmentSearchRewriter;
        private readonly ILogger<SqlServerSearchService> _logger;
        private readonly BitColumn _isMatch = new BitColumn("IsMatch");
        private readonly BitColumn _isPartial = new BitColumn("IsPartial");
        private readonly ISqlRetryService _sqlRetryService;
        private readonly SqlServerDataStoreConfiguration _sqlServerDataStoreConfiguration;
        private readonly FhirSqlServerConfiguration _fhirSqlServerConfiguration;
        private readonly SchemaInformation _schemaInformation;
        private readonly ICompressedRawResourceConverter _compressedRawResourceConverter;
        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
        private readonly ISqlQueryHashCalculator _queryHashCalculator;
        private readonly IFhirDataStore _fhirDataStore;
        private readonly IQueryPlanReuseChecker _queryPlanReuseChecker;
        private readonly SearchParameterSqlParser _searchParameterSqlParser;

        private static object _locker = new object();

        public SqlServerSearchService(
            ISearchOptionsFactory searchOptionsFactory,
            IFhirDataStore fhirDataStore,
            ISqlServerFhirModel model,
            CompartmentSearchRewriter compartmentSearchRewriter,
            SmartCompartmentSearchRewriter smartCompartmentSearchRewriter,
            ISqlRetryService sqlRetryService,
            IOptions<SqlServerDataStoreConfiguration> sqlServerDataStoreConfiguration,
            FhirSqlServerConfiguration fhirSqlServerConfiguration,
            SchemaInformation schemaInformation,
            RequestContextAccessor<IFhirRequestContext> requestContextAccessor,
            ICompressedRawResourceConverter compressedRawResourceConverter,
            ISqlQueryHashCalculator queryHashCalculator,
            IQueryPlanReuseChecker queryPlanReuseChecker,
            SearchParameterSqlParser searchParameterSqlParser,
            ILogger<SqlServerSearchService> logger)
            : base(searchOptionsFactory, fhirDataStore, logger)
        {
            EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            EnsureArg.IsNotNull(compartmentSearchRewriter, nameof(compartmentSearchRewriter));
            EnsureArg.IsNotNull(smartCompartmentSearchRewriter, nameof(smartCompartmentSearchRewriter));
            EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
            EnsureArg.IsNotNull(queryPlanReuseChecker, nameof(queryPlanReuseChecker));
            EnsureArg.IsNotNull(searchParameterSqlParser, nameof(searchParameterSqlParser));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _sqlServerDataStoreConfiguration = EnsureArg.IsNotNull(sqlServerDataStoreConfiguration?.Value, nameof(sqlServerDataStoreConfiguration));
            _fhirSqlServerConfiguration = EnsureArg.IsNotNull(fhirSqlServerConfiguration, nameof(fhirSqlServerConfiguration));
            _fhirDataStore = fhirDataStore;
            _model = model;
            _compartmentSearchRewriter = compartmentSearchRewriter;
            _smartCompartmentSearchRewriter = smartCompartmentSearchRewriter;
            _sqlRetryService = sqlRetryService;
            _queryHashCalculator = queryHashCalculator;
            _queryPlanReuseChecker = queryPlanReuseChecker;
            _searchParameterSqlParser = searchParameterSqlParser;
            _logger = logger;

            _schemaInformation = schemaInformation;
            _requestContextAccessor = requestContextAccessor;
            _compressedRawResourceConverter = compressedRawResourceConverter;

            InitializeProcessingFlags(logger);
        }

        internal bool StoredProcedureLayerIsEnabled { get; set; } = true;

        internal ISqlServerFhirModel Model => _model;

        private static void InitializeProcessingFlags(ILogger<SqlServerSearchService> logger)
        {
            lock (_locker)
            {
                if (_longRunningQueryDetails == null)
                {
                    _longRunningQueryDetails = new CachedParameter<SqlServerSearchService>(LongRunningQueryDetailsParameterId, 1, logger);
                }

                if (_longRunningThreshold == null)
                {
                    _longRunningThreshold = new CachedParameter<SqlServerSearchService>(LongRunningQueryDetailsThresholdId, LongRunningThresholdMillisecondsDefault, logger);
                }

                if (_referenceResourceTypeFilteredStats == null)
                {
                    // Default 0 (disabled): the 3-column reference-type filtered stat is only created
                    // when an operator adds a row to the Parameters table with this Id enabled.
                    _referenceResourceTypeFilteredStats = new CachedParameter<SqlServerSearchService>(ReferenceResourceTypeFilteredStatsParameterId, 0, logger);
                }
            }
        }

        public override async Task<SearchResult> SearchAsync(SearchOptions searchOptions, CancellationToken cancellationToken)
        {
            SqlSearchOptions sqlSearchOptions = new SqlSearchOptions(searchOptions);

            if (sqlSearchOptions.IsIncludesOperation)
            {
                var includesContinuationToken = IncludesContinuationToken.FromString(sqlSearchOptions.IncludesContinuationToken);
                if (includesContinuationToken == null)
                {
                    _logger.LogWarning("Bad Request (InvalidIncludesContinuationToken)");
                    throw new BadRequestException(Resources.InvalidIncludesContinuationToken);
                }

                sqlSearchOptions.SortQuerySecondPhase = includesContinuationToken.SortQuerySecondPhase ?? false;

                SearchResult includesSearchResult = await RunSearch(sqlSearchOptions, cancellationToken);

                if (includesSearchResult.Results.Count() < sqlSearchOptions.IncludeCount
                    && includesSearchResult.IncludesContinuationToken == null
                    && includesContinuationToken.SecondPhaseContinuationToken != null)
                {
                    // Continue to second phase of includes
                    sqlSearchOptions.IncludesContinuationToken = includesContinuationToken.SecondPhaseContinuationToken.ToJson();
                    sqlSearchOptions.IncludeCount = sqlSearchOptions.IncludeCount - includesSearchResult.Results.Count();
                    sqlSearchOptions.SortQuerySecondPhase = true;

                    var secondPhaseIncludesSearchResult = await RunSearch(sqlSearchOptions, cancellationToken);

                    var finalResults = new List<SearchResultEntry>();
                    finalResults.AddRange(includesSearchResult.Results);
                    finalResults.AddRange(secondPhaseIncludesSearchResult.Results);
                    includesSearchResult = new SearchResult(
                        finalResults,
                        secondPhaseIncludesSearchResult.ContinuationToken,
                        secondPhaseIncludesSearchResult.SortOrder,
                        secondPhaseIncludesSearchResult.UnsupportedSearchParameters,
                        includesContinuationToken: secondPhaseIncludesSearchResult.IncludesContinuationToken);
                }
                else if (includesSearchResult.Results.Count() >= sqlSearchOptions.IncludeCount
                    && includesSearchResult.IncludesContinuationToken != null
                    && includesContinuationToken.SecondPhaseContinuationToken != null)
                {
                    // We have reached the requested include count but there are more includes to be fetched.
                    // We need to preserve the second phase continuation token.
                    var newIncludesContinuationToken = IncludesContinuationToken.FromString(includesSearchResult.IncludesContinuationToken);

                    var combinedIncludesContinuationToken = new IncludesContinuationToken(
                        new object[]
                        {
                            newIncludesContinuationToken.MatchResourceTypeId,
                            newIncludesContinuationToken.MatchResourceSurrogateIdMin,
                            newIncludesContinuationToken.MatchResourceSurrogateIdMax,
                            newIncludesContinuationToken.IncludeResourceTypeId,
                            newIncludesContinuationToken.IncludeResourceSurrogateId,
                            includesContinuationToken.SortQuerySecondPhase,
                            includesContinuationToken.SecondPhaseContinuationToken,
                        }).ToJson();
                    includesSearchResult = new SearchResult(
                        includesSearchResult.Results,
                        includesSearchResult.ContinuationToken,
                        includesSearchResult.SortOrder,
                        includesSearchResult.UnsupportedSearchParameters,
                        includesContinuationToken: combinedIncludesContinuationToken);
                }
                else if (includesSearchResult.Results.Count() >= sqlSearchOptions.IncludeCount
                    && includesSearchResult.IncludesContinuationToken == null
                    && includesContinuationToken.SecondPhaseContinuationToken != null)
                {
                    // We have reached the requested include count and there are no more includes to be fetched.
                    // So the next page of results is from the second phase continuation token.

                    includesSearchResult = new SearchResult(
                        includesSearchResult.Results,
                        includesSearchResult.ContinuationToken,
                        includesSearchResult.SortOrder,
                        includesSearchResult.UnsupportedSearchParameters,
                        includesContinuationToken: includesContinuationToken.SecondPhaseContinuationToken.ToJson());
                }

                return includesSearchResult;
            }

            SearchResult searchResult = await RunSearch(sqlSearchOptions, cancellationToken);
            int resultCount = searchResult.Results.Count(r => r.SearchEntryMode == SearchEntryMode.Match);

            if (!sqlSearchOptions.IsSortWithFilter &&
                !sqlSearchOptions.SortHasMissingModifier &&
                !sqlSearchOptions.SortQuerySecondPhase &&
                searchResult.ContinuationToken == null &&
                resultCount <= sqlSearchOptions.MaxItemCount &&
                sqlSearchOptions.Sort != null &&
                sqlSearchOptions.Sort.Count > 0 &&
                sqlSearchOptions.Sort[0].searchParameterInfo.Code != KnownQueryParameterNames.LastUpdated)
            {
                // We seem to have run a sort which has returned less results than what max we can return.
                // Let's determine whether we need to execute another query or not.
                if ((sqlSearchOptions.Sort[0].sortOrder == SortOrder.Ascending)
                    || (sqlSearchOptions.Sort[0].sortOrder == SortOrder.Descending)
                    || (sqlSearchOptions.Sort[0].sortOrder == SortOrder.Descending && resultCount == 0 && !sqlSearchOptions.CountOnly))
                {
                    if (sqlSearchOptions.MaxItemCount - resultCount == 0)
                    {
                        // Check if more resources to be retrieved.
                        sqlSearchOptions.SortQuerySecondPhase = true;
                        sqlSearchOptions.MaxItemCount = 1;
                        var secondSearchResult = await RunSearch(sqlSearchOptions, cancellationToken);

                        // Since we are already returning MaxItemCount number of resources we don't want
                        // to execute another search right now just to drop all the resources. We will return
                        // a "special" ct so that we the subsequent request will be handled correctly.
                        var ct = (secondSearchResult.Results?.Any() ?? false) ? new ContinuationToken(new object[]
                            {
                                SqlSearchConstants.SortSentinelValueForCt,
                                0,
                            })
                            : null;

                        searchResult = new SearchResult(
                            searchResult.Results,
                            ct?.ToJson(),
                            searchResult.SortOrder,
                            searchResult.UnsupportedSearchParameters,
                            includesContinuationToken: searchResult.IncludesContinuationToken);
                    }
                    else
                    {
                        var finalResultsInOrder = new List<SearchResultEntry>();
                        finalResultsInOrder.AddRange(searchResult.Results);
                        sqlSearchOptions.SortQuerySecondPhase = true;
                        sqlSearchOptions.MaxItemCount -= resultCount;

                        var includesCount = searchResult.Results.Count(r => r.SearchEntryMode == SearchEntryMode.Include);
                        if (includesCount < sqlSearchOptions.IncludeCount)
                        {
                            sqlSearchOptions.IncludeCount -= includesCount;
                        }
                        else
                        {
                            sqlSearchOptions.IncludeContinuationTokenSearch = true;
                            sqlSearchOptions.IncludeCount = 0;
                        }

                        var secondSearchResult = await RunSearch(sqlSearchOptions, cancellationToken);

                        finalResultsInOrder.AddRange(secondSearchResult.Results);

                        var includesContinuationToken = searchResult.IncludesContinuationToken;

                        // If phase 2 didn't produce an includes continuation token but we know
                        // there are includes to fetch (IncludeContinuationTokenSearch was set because
                        // phase 1 exhausted the include budget), create one from phase 2's matched results.
                        var secondPhaseIncludesCt = secondSearchResult.IncludesContinuationToken;
                        if (secondPhaseIncludesCt == null
                            && sqlSearchOptions.IncludeContinuationTokenSearch
                            && sqlSearchOptions.IncludesOperationSupported
                            && secondSearchResult.Results.Any(r => r.SearchEntryMode == SearchEntryMode.Match))
                        {
                            var phase2Matches = secondSearchResult.Results
                                .Where(r => r.SearchEntryMode == SearchEntryMode.Match)
                                .ToList();
                            var firstMatch = phase2Matches.First().Resource;
                            var lastMatch = phase2Matches.Last().Resource;
                            var resourceTypeId = _model.GetResourceTypeId(firstMatch.ResourceTypeName);

                            secondPhaseIncludesCt = new IncludesContinuationToken(new object[]
                            {
                                resourceTypeId,
                                firstMatch.ResourceSurrogateId,
                                lastMatch.ResourceSurrogateId,
                                null,
                                null,
                                true, // SortQuerySecondPhase
                            }).ToJson();
                        }

                        if (secondPhaseIncludesCt != null)
                        {
                            if (includesContinuationToken == null)
                            {
                                includesContinuationToken = secondPhaseIncludesCt;
                            }
                            else
                            {
                                var firstToken = IncludesContinuationToken.FromString(includesContinuationToken);
                                var secondToken = IncludesContinuationToken.FromString(secondPhaseIncludesCt);
                                includesContinuationToken = new IncludesContinuationToken(new object[]
                                {
                                    firstToken.MatchResourceTypeId,
                                    firstToken.MatchResourceSurrogateIdMin,
                                    firstToken.MatchResourceSurrogateIdMax,
                                    firstToken.IncludeResourceTypeId,
                                    firstToken.IncludeResourceSurrogateId,
                                    false,
                                    secondToken,
                                }).ToJson();
                            }
                        }

                        searchResult = new SearchResult(
                            finalResultsInOrder,
                            secondSearchResult.ContinuationToken,
                            secondSearchResult.SortOrder,
                            secondSearchResult.UnsupportedSearchParameters,
                            includesContinuationToken: includesContinuationToken);
                    }
                }
            }

            // If we should include the total count of matching search results
            if (sqlSearchOptions.IncludeTotal == TotalType.Accurate && !sqlSearchOptions.CountOnly)
            {
                // If this is the first page and there aren't any more pages
                if (sqlSearchOptions.ContinuationToken == null && searchResult.ContinuationToken == null)
                {
                    // Count the match results on the page.
                    searchResult.TotalCount = searchResult.Results.Count(r => r.SearchEntryMode == SearchEntryMode.Match);
                }
                else
                {
                    try
                    {
                        // Otherwise, indicate that we'd like to get the count
                        sqlSearchOptions.CountOnly = true;

                        // And perform a second read.
                        var countOnlySearchResult = await RunSearch(sqlSearchOptions, cancellationToken);

                        searchResult.TotalCount = countOnlySearchResult.TotalCount;
                    }
                    finally
                    {
                        // Ensure search options is set to its original state.
                        sqlSearchOptions.CountOnly = false;
                    }
                }
            }

            return searchResult;
        }

        public override bool IsValidResourceType(string resourceType)
        {
            return _model.TryGetResourceTypeId(resourceType, out _);
        }
    }
}
