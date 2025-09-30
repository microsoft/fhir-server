// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using EnsureThat;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.QueryPlanCache
{
    public class Ewma
    {
        private readonly double _alpha;
        private readonly object _lock;
        private readonly double _minIterationsForDecision;
        private readonly string[] _metricNames;
        private readonly ConcurrentDictionary<string, double?> _ewmaScores;

        private long _iterations;

        public Ewma(string[] metricNames, int minIterationsForDecision, double alpha = 0.3)
        {
            _metricNames = EnsureArg.IsNotNull(metricNames, nameof(metricNames));
            _minIterationsForDecision = EnsureArg.IsGte(minIterationsForDecision, 0, nameof(minIterationsForDecision));
            _alpha = EnsureArg.IsGt(alpha, 0, nameof(alpha));

            _lock = new object();
            _iterations = 0;

            _ewmaScores = new ConcurrentDictionary<string, double?>();
            foreach (var metricName in metricNames)
            {
                _ewmaScores.TryAdd(metricName, null);
            }
        }

        public long Iterations => Interlocked.Read(ref _iterations);

        public void Update(string config, double newScore)
        {
            if (_ewmaScores.TryGetValue(config, out double? value))
            {
                _ewmaScores.AddOrUpdate(
                    config,
                    key => newScore,
                    (key, oldValue) =>
                    {
                        if (oldValue == null)
                        {
                            return newScore;
                        }

                        return (_alpha * newScore) + ((1 - _alpha) * oldValue.Value);
                    });
            }
            else
            {
                throw new ArgumentException($"Unknown config: {config}");
            }
        }

        public string GetBestMetric()
        {
            string metricName;

            if (_iterations < _minIterationsForDecision)
            {
                lock (_lock)
                {
                    // To make an informed decision, we need to have at least one score for each metric.
                    // With the current logic, we will execute each metric in a round-robin fashion until we reach the minimum number of iterations.
                    if (_iterations < _minIterationsForDecision)
                    {
                        metricName = _metricNames[_iterations % _metricNames.Length];
                        _iterations++;
                        return metricName;
                    }
                }
            }

            metricName = _ewmaScores
                .Where(score => score.Value != null)
                .OrderBy(score => score.Value)
                .Select(score => score.Key)
                .FirstOrDefault();

            Interlocked.Increment(ref _iterations);

            return metricName;
        }
    }
}
