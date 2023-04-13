// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.SqlServer.Features.Search;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class TestSqlHashCalculator : ISqlQueryHashCalculator
    {
        public string MostRecentSqlQuery { get; set; }

        public string MostRecentSqlHash { get; set; }

        public string CalculateHash(string query)
        {
            MostRecentSqlQuery = query;
            MostRecentSqlHash = query.ComputeHash();

            return MostRecentSqlHash;
        }
    }
}
