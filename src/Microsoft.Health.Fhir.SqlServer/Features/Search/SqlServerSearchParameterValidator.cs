// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

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
                // Create/update is the entry point for a vector SearchParameter, so reject one the server cannot serve
                // here rather than accepting it and failing every query that reaches it.
                if (!_vectorSearchConfiguration.Enabled)
                {
                    errorMessage = "Vector SearchParameters cannot be created or updated because semantic search is not enabled on this server.";
                    return false;
                }

                return VectorSearchParameterResolver.TryValidateDefinition(searchParameter, _vectorSearchConfiguration, out errorMessage);
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
