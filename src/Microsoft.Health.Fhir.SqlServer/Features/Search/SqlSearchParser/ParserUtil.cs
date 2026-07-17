// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    internal class ParserUtil
    {
        public static void AddHistoryAndDeletedCheck(SqlQueryBuilder builder, string tableAlias, bool includeHistory = false, bool includeDeleted = false)
        {
            if (!includeHistory)
            {
                builder.And($"{tableAlias}.IsHistory = 0");
            }

            if (!includeDeleted)
            {
                builder.And($"{tableAlias}.IsDeleted = 0");
            }
        }

        public static void AddUnionCte(SqlQueryBuilder builder, string cteName, IList<string> targetCtes, bool includeSort = false)
        {
            builder.BeginCte(cteName);
            builder.AppendLine($"SELECT * FROM {targetCtes[0]}");

            foreach (var includeCteName in targetCtes.Skip(1))
            {
                builder.AppendLine("UNION ALL");
                builder.IncreaseIndent();
                builder.AppendLine($"SELECT *{(includeSort ? ", SortValue = NULL" : string.Empty)} FROM {includeCteName}");
                builder.Where($"NOT EXISTS (SELECT * FROM {targetCtes[0]} base WHERE base.ResourceSurrogateId = {includeCteName}.ResourceSurrogateId)");
                builder.DecreaseIndent();
            }

            builder.EndCte();
        }
    }
}
