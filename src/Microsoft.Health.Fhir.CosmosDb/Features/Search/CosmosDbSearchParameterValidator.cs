// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Search
{
    internal class CosmosDbSearchParameterValidator : IDataStoreSearchParameterValidator
    {
        // Currently Cosmos DB has not additional validation steps to perform specific to the
        // data store for validation of a SearchParameter
        public bool ValidateSearchParameter(SearchParameterInfo searchParameter, out string errorMessage)
        {
            errorMessage = null;
            return true;
        }
    }
}
