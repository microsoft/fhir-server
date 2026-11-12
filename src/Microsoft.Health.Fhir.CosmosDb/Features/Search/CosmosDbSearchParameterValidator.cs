// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Search
{
    internal class CosmosDbSearchParameterValidator : IDataStoreSearchParameterValidator
    {
        // Cosmos DB has no data store specific validation for ordinary SearchParameters. A vector SearchParameter can
        // only be served by the SQL Server data store, so reject it rather than accept a definition no query can use.
        public bool ValidateSearchParameter(SearchParameterInfo searchParameter, out string errorMessage)
        {
            EnsureArg.IsNotNull(searchParameter, nameof(searchParameter));

            if (searchParameter.VectorConfig != null)
            {
                errorMessage = "Vector SearchParameters are not supported because semantic search is only available with the SQL Server data store.";
                return false;
            }

            errorMessage = null;
            return true;
        }
    }
}
