// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class EnumerableExtensions
    {
        /// <summary>
        /// Creates the cartesian product.
        /// </summary>
        /// <typeparam name="TSource">The type of the elements of the input sequences.</typeparam>
        /// <param name="sequences">The input sequence.</param>
        /// <returns>An <see cref="IEnumerable{T}"/> that contains the cartesian product of the input sequences.</returns>
        public static IEnumerable<IEnumerable<TSource>> CartesianProduct<TSource>(this IEnumerable<IEnumerable<TSource>> sequences)
        {
            EnsureArg.IsNotNull(sequences, nameof(sequences));

            IEnumerable<IEnumerable<TSource>> emptyProduct = new[] { Enumerable.Empty<TSource>() };

            return sequences.Aggregate(
                emptyProduct,
                (accumulator, sequence) =>
                {
                    return accumulator.SelectMany(a => sequence.Select(s => a.Concat(Enumerable.Repeat(s, 1))));
                });
        }

        /// <summary>
        /// Generates a sequence that contains one value.
        /// </summary>
        /// <typeparam name="TResult">The element type.</typeparam>
        /// <param name="element">The element to return.</param>
        /// <returns>An <see cref="IEnumerable{T}"/> that contains one value.</returns>
        public static IEnumerable<TResult> AsEnumerable<TResult>(this TResult element)
        {
            yield return element;
        }
    }
}
