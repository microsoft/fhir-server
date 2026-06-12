// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Core.Extensions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    internal class SqlQueryHashCalculator : ISqlQueryHashCalculator
    {
        public string CalculateHash(string query)
        {
            return RemoveParametersHash(query).ComputeHash();
        }

        // This method negates effect of the AddParametersHash(). This is done this way to keep current SQL generator logic.
        internal static string RemoveParametersHash(string query)
        {
            var hashStartIndex = query.IndexOf(SqlSearchConstants.ParametersHashStart, StringComparison.OrdinalIgnoreCase);
            if (hashStartIndex < 0) // no parameters hash
            {
                return query;
            }

            var hashEndIndex = query[hashStartIndex..].IndexOf(SqlSearchConstants.ParametersHashEnd, StringComparison.OrdinalIgnoreCase);
            var hashLine = query[hashStartIndex..(hashStartIndex + hashEndIndex + SqlSearchConstants.ParametersHashEnd.Length)];
            return query.Replace(hashLine, string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }
}
