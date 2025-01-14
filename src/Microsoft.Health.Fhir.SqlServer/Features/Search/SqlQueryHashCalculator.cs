// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Core.Extensions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    internal class SqlQueryHashCalculator : ISqlQueryHashCalculator
    {
        public string CalculateHash(string query)
        {
            return query.ComputeHash();
        }
    }
}
