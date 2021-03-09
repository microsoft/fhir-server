// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    internal class SqlServerSearchParameterValidator : IDataStoreSearchParameterValidator
    {
        private readonly SearchParameterToSearchValueTypeMap _searchParameterToSearchValueTypeMap;

        public SqlServerSearchParameterValidator(SearchParameterToSearchValueTypeMap searchParameterToSearchValueTypeMap)
        {
            EnsureArg.IsNotNull(searchParameterToSearchValueTypeMap, nameof(searchParameterToSearchValueTypeMap));

            _searchParameterToSearchValueTypeMap = searchParameterToSearchValueTypeMap;
        }

        public bool ValidateSearchParameter(SearchParameterInfo searchParameter, out string errorMessage)
        {
            EnsureArg.IsNotNull(searchParameter, nameof(searchParameter));
            errorMessage = null;

            switch (searchParameter.Type)
            {
                case SearchParamType.Token:
                case SearchParamType.Date:
                case SearchParamType.Number:
                case SearchParamType.Quantity:
                case SearchParamType.Reference:
                case SearchParamType.String:
                case SearchParamType.Uri:
                    return true;
                case SearchParamType.Composite:
                    Type searchValueType = _searchParameterToSearchValueTypeMap.GetSearchValueType(searchParameter);
                    if (searchValueType == typeof(ValueTuple<TokenSearchValue, QuantitySearchValue>)
                        || searchValueType == typeof(ValueTuple<ReferenceSearchValue, TokenSearchValue>)
                        || searchValueType == typeof(ValueTuple<TokenSearchValue, DateTimeSearchValue>)
                        || searchValueType == typeof(ValueTuple<TokenSearchValue, StringSearchValue>)
                        || searchValueType == typeof(ValueTuple<TokenSearchValue, NumberSearchValue, NumberSearchValue>))
                        {
                            return true;
                        }
                        else
                        {
                            errorMessage = string.Format(Resources.SearchParameterTypeNotSupportedBySQLServer, searchParameter.Type);
                            return false;
                        }

                default:
                    errorMessage = string.Format(Resources.SearchParameterTypeNotSupportedBySQLServer, searchParameter.Type);
                    return false;
            }
        }
    }
}
