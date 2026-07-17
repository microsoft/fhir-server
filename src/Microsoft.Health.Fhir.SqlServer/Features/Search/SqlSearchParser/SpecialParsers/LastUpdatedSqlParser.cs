// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.SqlServer.Management.XEvent;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser.SpecialParsers
{
    public class LastUpdatedSqlParser : ISqlParser
    {
        public void Parse(string name, string value, ParserOptions options)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentNullException(nameof(value));
            }

            var sqlBuilder = options.SqlQueryBuilder;
            sqlBuilder.BeginCte($"cte{options.CteNumber}");
            sqlBuilder.Select("r.ResourceTypeId", "r.ResourceSurrogateId");
            sqlBuilder.From(options.LastCteName ?? "dbo.Resource", "r");

            var dateTime = DateTimeSqlParser.ParseValue(value, out var modifier);
            var minSurrogateId = ResourceSurrogateIdHelper.ToSurrogateId(dateTime.Start);
            var maxSurrogateId = ResourceSurrogateIdHelper.ToSurrogateId(dateTime.End.AddMilliseconds(1));

            // Because surrogate id is a range for the same datetime, different operators need to be handled accordingly.
            var whereClause = modifier switch
            {
                "gt" => $"r.ResourceSurrogateId >= {maxSurrogateId}", // greater than means the start of the next millisecond, so max surrogate id is included
                "ge" => $"r.ResourceSurrogateId >= {minSurrogateId}",
                "lt" => $"r.ResourceSurrogateId < {minSurrogateId}",
                "le" => $"r.ResourceSurrogateId < {maxSurrogateId}",
                "sa" => $"r.ResourceSurrogateId > {maxSurrogateId}",
                "eb" => $"r.ResourceSurrogateId < {minSurrogateId}",
                "ne" => $"(r.ResourceSurrogateId >= {maxSurrogateId} OR r.ResourceSurrogateId < {minSurrogateId})",
                "eq" => $"r.ResourceSurrogateId >= {minSurrogateId} AND r.ResourceSurrogateId < {maxSurrogateId}",
                _ => throw new ArgumentException($"Invalid operator '{modifier}' for lastUpdated search parameter."),
            };

            sqlBuilder.Where(whereClause);

            // Add base filters only on the first CTE
            if (options.LastCteName == null)
            {
                ParserUtil.AddHistoryAndDeletedCheck(sqlBuilder, "r");

                if (options.ResourceTypes != null && options.ResourceTypes.Count > 0)
                {
                    var resourceTypeIds = string.Join(", ", options.ResourceTypes);
                    sqlBuilder.And($"r.ResourceTypeId IN ({resourceTypeIds})");
                }

                if (options.ContinuationToken != null)
                {
                    sqlBuilder.And($"r.ResourceSurrogateId {(options.SortDescending ? "<" : ">")} {options.ContinuationToken.ResourceSurrogateId}");

                    if (options.ContinuationToken.ResourceTypeId != null)
                    {
                        sqlBuilder.And($"r.ResourceTypeId {(options.SortDescending ? "<" : ">")}= {options.ContinuationToken.ResourceTypeId}");
                    }
                }
            }

            sqlBuilder.EndCte();
        }
    }
}
