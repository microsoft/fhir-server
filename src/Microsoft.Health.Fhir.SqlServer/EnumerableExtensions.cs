// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections;
using System.Collections.Generic;

namespace Microsoft.Health.Fhir.SqlServer
{
    internal static class EnumerableExtensions
    {
        /// <summary>
        /// If the given sequence is null or empty, returns null. Otherwise returns
        /// an equivalent sequence.
        /// </summary>
        /// <typeparam name="T">The element type</typeparam>
        /// <param name="enumerable">The input sequence</param>
        /// <returns>An equivalent sequence, or null if given one is empty or null.</returns>
        internal static IEnumerable<T> NullIfEmpty<T>(this IEnumerable<T> enumerable)
        {
            if (enumerable == null)
            {
                return null;
            }

            IEnumerator<T> enumerator = enumerable.GetEnumerator();
            if (!enumerator.MoveNext())
            {
                return null;
            }

            return new EnumerableFromStartedEnumerator<T>(enumerator, enumerable);
        }

        private class EnumerableFromStartedEnumerator<T> : IEnumerable<T>
        {
            private readonly IEnumerable<T> _original;
            private IEnumerator<T> _startedEnumerator;

            public EnumerableFromStartedEnumerator(IEnumerator<T> startedEnumerator, IEnumerable<T> original)
            {
                _original = original;
                _startedEnumerator = startedEnumerator;
            }

            public IEnumerator<T> GetEnumerator()
            {
                IEnumerable<T> Inner(IEnumerator<T> e)
                {
                    try
                    {
                        do
                        {
                            yield return e.Current;
                        }
                        while (e.MoveNext());
                    }
                    finally
                    {
                        e.Dispose();
                    }
                }

                if (_startedEnumerator != null)
                {
                    IEnumerator<T> e = _startedEnumerator;
                    _startedEnumerator = null;
                    return Inner(e).GetEnumerator();
                }

                return _original.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return ((IEnumerable)this).GetEnumerator();
            }
        }
    }
}
