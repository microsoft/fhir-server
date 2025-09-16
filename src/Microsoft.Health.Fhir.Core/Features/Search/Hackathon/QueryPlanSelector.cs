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
                var newEwma = new Ewma(_settings, MinIterationsForDecision, EwmaAlpha);
                _ewmaByHash.Add(hash, newEwma);

                return newEwma;
            }
        }
    }
}
