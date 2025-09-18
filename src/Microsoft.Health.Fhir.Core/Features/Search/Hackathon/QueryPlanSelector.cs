// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.Hackathon
{
    public sealed class QueryPlanSelector : IQueryPlanSelector<bool>
    {
        private const int MaxEwmaEntries = 500;
        private const int MinIterationsForDecision = 6;
        private const double EwmaAlpha = 0.3;

        // Possible settings for enabling or disabling query plan caching.
        // 'False' is the first element to ensure it is the default when EWMA scores are equal.
        private readonly string[] _settings = new[] { "False", "True" };

        private readonly object _lock;
        private readonly Dictionary<string, Ewma> _ewmaByHash;

        public QueryPlanSelector()
        {
            _lock = new object();
            _ewmaByHash = new Dictionary<string, Ewma>();
        }

        public bool GetQueryPlanCachingSetting(string hash)
        {
            EnsureArg.IsNotNullOrEmpty(hash, nameof(hash));

            Ewma ewma;
            lock (_lock)
            {
                ewma = GetEwma(hash);
            }

            // Hard max limit to avoid memory leaks while using dictionaries.
            // TODO: use dotnet memory cache with expiration instead of limiting the size of the dictionary.
            if (ewma == null)
            {
                if (IsMaxLimitReached())
                {
                    return false;
                }
                else
                {
                    throw new InvalidOperationException($"EWMA instance for hash '{hash}' should have been created.");
                }
            }

            string metricNameValue = ewma.GetBestMetric();

            if (bool.TryParse(metricNameValue, out bool metric))
            {
                return metric;
            }

            throw new InvalidOperationException($"Failed to parse best metric name '{metricNameValue}' to bool.");
        }

        public void ReportExecutionTime(string hash, bool metricName, double executionTimeMs)
        {
            EnsureArg.IsNotNullOrEmpty(hash, nameof(hash));

            Ewma ewma = GetEwma(hash);

            ewma.Update(metricName.ToString(), executionTimeMs);
        }

        private Ewma GetEwma(string hash)
        {
            if (_ewmaByHash.TryGetValue(hash, out Ewma ewma))
            {
                return ewma;
            }
            else
            {
                if (IsMaxLimitReached())
                {
                    // Limit the size of the dictionary to avoid unbounded memory growth.
                    // In a real-world scenario, consider using a more sophisticated eviction policy.
                    return null;
                }

                var newEwma = new Ewma(_settings, MinIterationsForDecision, EwmaAlpha);
                _ewmaByHash.Add(hash, newEwma);

                return newEwma;
            }
        }

        private bool IsMaxLimitReached() => _ewmaByHash.Count >= MaxEwmaEntries;
    }
}
