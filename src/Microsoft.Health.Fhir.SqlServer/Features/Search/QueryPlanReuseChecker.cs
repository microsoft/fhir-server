// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    internal class QueryPlanReuseChecker : IQueryPlanReuseChecker, INotificationHandler<SearchParametersInitializedNotification>
    {
        private readonly Regex _skewedParameterRegex = new Regex(@"^ST_.*_WHERE_ResourceTypeId_(\d*)_SearchParamId_(\d*)$");
        private readonly double _refreshPeriod = 3600;
        private readonly double _skewThreshold = 30;

        // Holds a set of urls of skewed search parameters.
        private HashSet<string> _skewedParameters = new HashSet<string>();
        private readonly ISqlRetryService _sqlRetryService;
        private readonly FhirTimer _timer;
        private readonly ILogger<QueryPlanReuseChecker> _logger;

        private bool _isInitialized = false;
        private bool _storageReady = false;

        public QueryPlanReuseChecker(ISqlRetryService sqlRetryService, ILogger<QueryPlanReuseChecker> logger)
        {
            _sqlRetryService = EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));

            _timer = new FhirTimer(_logger);

            // this should wait for storage to be ready.
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            StartRefreshTimer();
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        }

        public bool CanReuseQueryPlan(SearchOptions searchOptions)
        {
            if (!_isInitialized)
            {
                return true;
            }

            // Check the skew of the search parameters. If the search parameters are skewed, the query plan should not be reused.
            var parameters = searchOptions.SearchParameters;

            foreach (var parameter in parameters.Where(p => _skewedParameters.Contains(p.Url.OriginalString)))
            {
                _logger.LogInformation("Search parameter {SearchParameter} is skewed. Query plan will not be reused.", parameter.Url.OriginalString);
                return false;
            }

            return true;
        }

        public Task Handle(SearchParametersInitializedNotification notification, CancellationToken cancellationToken)
        {
            _storageReady = true;
            return Task.CompletedTask;
        }

        private async Task StartRefreshTimer()
        {
            try
            {
                while (!_storageReady)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }

                _logger.LogInformation("Starting QueryPlanReuseChecker refresh timer.");

                await RefreshCache(CancellationToken.None);
                await _timer.ExecuteAsync("QueryPlanReuseChecker.RefreshCache", _refreshPeriod, RefreshCache, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QueryPlanReuseChecker refresh loop failed.");
            }
        }

        private async Task RefreshCache(CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand();
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = "dbo.GetResourceSearchParamStatsProperties";

            var results = await cmd.ExecuteReaderAsync<(string ResourceTypeId, string SearchParamId)>(
                _sqlRetryService,
                (reader) =>
                {
                    try
                    {
                        var name = reader.GetString(0);
                        var skew = reader.GetDouble(2);

                        if (skew > _skewThreshold)
                        {
                            var match = _skewedParameterRegex.Match(name);
                            if (match.Success)
                            {
                                var resourceTypeId = match.Groups[1].Value;
                                var searchParamId = match.Groups[2].Value;

                                return (resourceTypeId, searchParamId);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Statistic missing value");
                    }

                    return (string.Empty, string.Empty);
                },
                _logger,
                CancellationToken.None);

            if (results == null || !results.Any(r => !string.IsNullOrEmpty(r.ResourceTypeId) && !string.IsNullOrEmpty(r.SearchParamId)))
            {
                _logger.LogInformation("No skewed search parameters found. Query plan reuse will not be affected.");
                _skewedParameters = new HashSet<string>();
                _isInitialized = true;
                return;
            }

            var searchParamIds = results.Where(r => !string.IsNullOrEmpty(r.ResourceTypeId) && !string.IsNullOrEmpty(r.SearchParamId)).Select(r => r.SearchParamId).Distinct().Aggregate((a, b) => a + "," + b);

            using var cmd2 = new SqlCommand();
            cmd2.CommandType = CommandType.Text;
#pragma warning disable CA2100 // Review SQL queries for security vulnerabilities. The searchParamIds are coming from the database and will be integers.
            cmd2.CommandText = $"SELECT SearchParamId, Uri FROM SearchParam WHERE SearchParamId IN ({searchParamIds})";
#pragma warning restore CA2100 // Review SQL queries for security vulnerabilities

            var searchParamUrls = await cmd2.ExecuteReaderAsync<(string SearchParamId, string Uri)>(
                _sqlRetryService,
                (reader) =>
                {
                    var searchParamId = reader.GetString(0);
                    var uri = reader.GetString(1);
                    return (searchParamId, uri);
                },
                _logger,
                CancellationToken.None);

            var skewedParamsByUri = results.Where(r => !string.IsNullOrEmpty(r.ResourceTypeId) && !string.IsNullOrEmpty(r.SearchParamId)).Select(r =>
            {
                var searchParamId = r.SearchParamId;
                var uri = searchParamUrls.FirstOrDefault(s => s.SearchParamId == searchParamId).Uri;
                return (uri, r.ResourceTypeId);
            }).Select(r => r.uri).Distinct();

            _skewedParameters = skewedParamsByUri.ToHashSet();
            _isInitialized = true;
        }
    }
}
