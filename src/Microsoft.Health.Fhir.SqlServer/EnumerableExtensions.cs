// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections;
using System.Collections.Generic;
using EnsureThat;

namespace Microsoft.Health.Fhir.SqlServer
{
    internal static class EnumerableExtensions
    {
        internal static IEnumerable<T> NullIfEmpty<T>(this IEnumerable<T> enumerable)
        {
            EnsureArg.IsNotNull(enumerable, nameof(enumerable));

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
