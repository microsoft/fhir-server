// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    internal static class EnumerableExtensions
    {
        public static IEnumerable<IEnumerable<T>> TakeBatch<T>(this IEnumerable<T> collection, int batchSize)
        {
            var batch = new List<T>(batchSize);

            foreach (T item in collection)
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
    }
}
