// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using LinqExpression = System.Linq.Expressions.Expression;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal abstract class CompositeSearchParamRowGenerator<TSearchValue, TRow> : MergeSearchParameterRowGenerator<TSearchValue, TRow>
        where TSearchValue : ITuple
        where TRow : struct
    {
        private readonly Func<EnumeratorWrapper<ISearchValue>, TSearchValue> _converter = CreateConverterFunc();

        protected CompositeSearchParamRowGenerator(SqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap)
        {
        }

        protected override IEnumerable<TSearchValue> ConvertSearchValue(SearchIndexEntry entry)
        {
            var compositeSearchValue = (CompositeSearchValue)entry.Value;

            foreach (var components in compositeSearchValue.Components.CartesianProduct())
            {
                using (IEnumerator<ISearchValue> enumerator = components.GetEnumerator())
                {
                    yield return _converter(new EnumeratorWrapper<ISearchValue>(enumerator));
                }
            }
        }

        /// <summary>
        /// Creates a function that takes the components of a composite search parameter as an
        /// enumerator and creates a ValueTuple with fields for each component.
        /// </summary>
        /// <returns>The generated function.</returns>
        private static Func<EnumeratorWrapper<ISearchValue>, TSearchValue> CreateConverterFunc()
        {
            var parameter = LinqExpression.Parameter(typeof(EnumeratorWrapper<ISearchValue>));
            MethodInfo nextValueMethod = parameter.Type.GetMethod(nameof(EnumeratorWrapper<ISearchValue>.NextValue));
            ConstructorInfo constructorInfo = typeof(TSearchValue).GetConstructors().Single();

            return LinqExpression.Lambda<Func<EnumeratorWrapper<ISearchValue>, TSearchValue>>(
                LinqExpression.New(
                    constructorInfo,
                    constructorInfo.GetParameters().Select(p => LinqExpression.Convert(
                        LinqExpression.Call(parameter, nextValueMethod),
                        p.ParameterType))),
                parameter).Compile();
        }

        /// <summary>
        /// Helper class to make the generated code in <see cref="CreateConverterFunc"/>
        /// a little simpler.
        /// </summary>
        /// <typeparam name="T">The element type</typeparam>
        private struct EnumeratorWrapper<T>
        {
            private readonly IEnumerator<T> _enumerator;

            public EnumeratorWrapper(IEnumerator<T> enumerator)
            {
                _enumerator = enumerator;
            }

            public T NextValue()
            {
                _enumerator.MoveNext();
                return _enumerator.Current;
            }
        }
    }
}
