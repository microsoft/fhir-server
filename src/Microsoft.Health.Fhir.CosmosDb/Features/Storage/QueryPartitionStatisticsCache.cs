// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    /// <summary>
    /// Maintains an LRU cache of <see cref="QueryPartitionStatistics"/>, keyed by search expression ignoring values in the expression.
    /// </summary>
    public sealed class QueryPartitionStatisticsCache : IDisposable
    {
        private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 512 });

        internal QueryPartitionStatistics GetQueryPartitionStatistics(Expression expression)
        {
            return _cache.GetOrCreate(
                new ExpressionWrapper(expression),
                e =>
                {
                    e.Size = 1;
                    return new QueryPartitionStatistics();
                });
        }

        public void Dispose()
        {
            _cache?.Dispose();
        }

        private class ExpressionWrapper
        {
            private readonly int _hashCode;
            private readonly Expression _expression;

            public ExpressionWrapper(Expression expression)
            {
                EnsureArg.IsNotNull(expression, nameof(expression));

                _expression = expression;

                HashCode hashCode = default;
                expression.AddValueInsensitiveHashCode(ref hashCode);
                _hashCode = hashCode.ToHashCode();
            }

            public override int GetHashCode() => _hashCode;

            public override bool Equals(object obj) => obj is ExpressionWrapper e && _expression.ValueInsensitiveEquals(e._expression);
        }
    }
}
