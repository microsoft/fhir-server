// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using SearchParamType = Microsoft.Health.Fhir.ValueSets.SearchParamType;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// Maintains a mapping from search parameters to a "representative" type. This is
    /// either one that implements ISearchValue, or for composites, a ValueTuple with the component types as type arguments,
    /// for example: <see cref="ValueTuple{UriSearchValue}"/>
    /// </summary>
    internal class SearchParameterToSearchValueTypeMap
    {
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ConcurrentDictionary<SearchParameterInfo, Type> _map = new ConcurrentDictionary<SearchParameterInfo, Type>();

        public SearchParameterToSearchValueTypeMap(ISearchParameterDefinitionManager searchParameterDefinitionManager)
        {
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
        }

        public Type GetSearchValueType(SearchParameterInfo searchParameter)
        {
            if (!_map.TryGetValue(searchParameter, out Type type))
            {
                type = GetSearchValueTypeImpl(searchParameter);

                _map.TryAdd(searchParameter, type);
            }

            return type;
        }

        public Type GetSearchValueType(SearchIndexEntry searchIndexEntry)
        {
            if (searchIndexEntry.Value is CompositeSearchValue)
            {
                return GetSearchValueType(searchIndexEntry.SearchParameter);
            }

            Type searchValueType = searchIndexEntry.Value.GetType();

            Debug.Assert(searchValueType == GetSearchValueType(searchIndexEntry.SearchParameter), "Getting the search value type from the search parameter produced a different result from calling searchValue.GetType()");

            return searchValueType;
        }

        private Type GetSearchValueTypeImpl(SearchParameterInfo searchParameter)
        {
            switch (searchParameter.Type)
            {
                case SearchParamType.Number:
                    return typeof(NumberSearchValue);
                case SearchParamType.Date:
                    return typeof(DateTimeSearchValue);
                case SearchParamType.String:
                    return typeof(StringSearchValue);
                case SearchParamType.Token:
                    return typeof(TokenSearchValue);
                case SearchParamType.Reference:
                    return typeof(ReferenceSearchValue);
                case SearchParamType.Quantity:
                    return typeof(QuantitySearchValue);
                case SearchParamType.Uri:
                    return typeof(UriSearchValue);
                case SearchParamType.Composite:
                    return typeof(Tuple).Assembly.GetType($"{typeof(ValueTuple).FullName}`{searchParameter.Component.Count}", throwOnError: true)
                        .MakeGenericType(searchParameter.Component.Select(c => GetSearchValueType(_searchParameterDefinitionManager.GetSearchParameter(c.DefinitionUrl))).ToArray());
                default:
                    throw new ArgumentOutOfRangeException(searchParameter.Code);
            }
        }
    }
}
