// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    public interface ISqlQueryHashCalculator
    {
        /// <summary>
        /// Given a string that represents a SQL query, this returns the calculated hash of that query.
        /// </summary>
        /// <param name="query">The SQL query as text</param>
        /// <returns>A string hash value.</returns>
        string CalculateHash(string query);
    }
}
