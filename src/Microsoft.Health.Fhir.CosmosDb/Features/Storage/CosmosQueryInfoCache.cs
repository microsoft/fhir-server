// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using EnsureThat;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public class CosmosQueryInfoCache
    {
        private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 512 });

        internal QueryPartitionStatistics GetQueryPartitionStatistics(string queryText)
        {
            return _cache.GetOrCreate(
                queryText,
                e =>
                {
                    e.Size = 1;
                    return new QueryPartitionStatistics();
                });
        }

        internal QueryPartitionStatistics GetQueryPartitionStatistics(Expression expression)
        {
            throw new IOException("sssss");
        }

        private class ExpressionWrapper
        {
            private readonly int _hashCode;

            public ExpressionWrapper(Expression expression)
            {
                EnsureArg.IsNotNull(expression, nameof(expression));

                Expression = expression;

                HashCode hashCode = default;

                Expression.AddValueInsensitiveHashCode(ref hashCode);
                _hashCode = hashCode.ToHashCode();
            }

            public Expression Expression { get; }

            public override int GetHashCode() => _hashCode;
        }
    }
}
