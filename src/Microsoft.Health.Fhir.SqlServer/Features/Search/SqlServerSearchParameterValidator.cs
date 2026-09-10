// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    internal class SqlServerSearchParameterValidator : IDataStoreSearchParameterValidator
    {
        private readonly SearchParameterToSearchValueTypeMap _searchParameterToSearchValueTypeMap;
        private readonly VectorSearchConfiguration _vectorSearchConfiguration;

        public SqlServerSearchParameterValidator(
            SearchParameterToSearchValueTypeMap searchParameterToSearchValueTypeMap,
            IOptions<VectorSearchConfiguration> vectorSearchConfiguration)
        {
            EnsureArg.IsNotNull(searchParameterToSearchValueTypeMap, nameof(searchParameterToSearchValueTypeMap));

            _searchParameterToSearchValueTypeMap = searchParameterToSearchValueTypeMap;
            _vectorSearchConfiguration = EnsureArg.IsNotNull(vectorSearchConfiguration, nameof(vectorSearchConfiguration)).Value;
        }

        public bool ValidateSearchParameter(SearchParameterInfo searchParameter, out string errorMessage)
        {
            EnsureArg.IsNotNull(searchParameter, nameof(searchParameter));
            errorMessage = null;

            if (searchParameter.VectorConfig != null)
            {
                if (searchParameter.Type == SearchParamType.Special)
                {
                    if (_vectorSearchConfiguration.TryResolveChunkSettings(searchParameter.VectorConfig, out _, out _, out string chunkSettingsError))
                    {
                        return true;
                    }

                    errorMessage = $"Vector SearchParameter has invalid effective chunk settings. {chunkSettingsError}";
                    return false;
                }

                errorMessage = string.Format(Resources.SearchParameterTypeNotSupportedBySQLServer, searchParameter.Type);
                return false;
            }

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
