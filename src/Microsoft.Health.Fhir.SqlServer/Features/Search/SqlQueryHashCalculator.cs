// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;

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
            if (query == null)
            {
                throw new BadRequestException(nameof(query));
            }

            var hashStartIndex = query.IndexOf(SqlSearchConstants.ParametersHashStart, StringComparison.OrdinalIgnoreCase);
            if (hashStartIndex < 0) // no parameters hash
            {
                return query;
            }

            var hashEndIndex = query[hashStartIndex..].IndexOf(SqlSearchConstants.ParametersHashEnd, StringComparison.OrdinalIgnoreCase);
            var hashLineEnd = hashStartIndex + hashEndIndex + SqlSearchConstants.ParametersHashEnd.Length;
            if (hashLineEnd < query.Length)
            {
                if (query[hashLineEnd] == '\r')
                {
                    hashLineEnd++;
                    if (hashLineEnd < query.Length && query[hashLineEnd] == '\n')
                    {
                        hashLineEnd++;
                    }
                }
                else if (query[hashLineEnd] == '\n')
                {
                    hashLineEnd++;
                }
            }

            return string.Concat(query.AsSpan(0, hashStartIndex), query.AsSpan(hashLineEnd));
        }
    }
}
