// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    internal static class EnumerableExtensions
    {
        public static IEnumerable<IEnumerable<T>> TakeBatch<T>(this IEnumerable<T> input, int batchSize)
        {
            if (input is ICollection<T> inputCollection && inputCollection.Count <= batchSize)
            {
                if (inputCollection.Count > 0)
                {
                    yield return input;
                }

                yield break;
            }

            var batch = new List<T>(batchSize);

            foreach (T item in input)
            {
                batch.Add(item);
                if (batch.Count >= batchSize)
                {
                    yield return batch;
                    batch = new List<T>(batchSize);
                }
            }

            if (batch.Count > 0)
            {
                yield return batch;
            }
        }

        public static IEnumerable<TResult> SelectParallel<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> selector, int degreeOfParallelism)
        {
            if (degreeOfParallelism < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(degreeOfParallelism));
            }

            OrderablePartitioner<TSource> partitioner = Partitioner.Create(source);

            return partitioner.AsParallel()
                .WithDegreeOfParallelism(degreeOfParallelism)
                .Select(selector);
        }
    }
}
