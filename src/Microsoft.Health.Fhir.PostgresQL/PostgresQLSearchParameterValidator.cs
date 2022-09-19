// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.PostgresQL
{
    public class PostgresQLSearchParameterValidator : IDataStoreSearchParameterValidator
    {
        public bool ValidateSearchParameter(SearchParameterInfo searchParameter, out string errorMessage)
        {
            throw new NotImplementedException();
        }
    }
}
