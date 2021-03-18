// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

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

            var factory = new SearchParamTableExpressionQueryGeneratorFactory(_searchParameterToSearchValueTypeMap);

            try
            {
                factory.GetGenerator(searchParameter);
                return true;
            }
            catch
            {
                errorMessage = string.Format(Resources.SearchParameterTypeNotSupportedBySQLServer, searchParameter.Type);
                return false;
            }
        }
    }
}
