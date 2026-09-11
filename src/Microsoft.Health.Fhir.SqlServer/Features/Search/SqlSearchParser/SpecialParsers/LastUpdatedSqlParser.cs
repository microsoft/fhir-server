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
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
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
            var cteName = options.ChainLevel == 0 ? $"cte{options.CteNumber}" : $"cte{options.CteNumber}chain{options.ChainLevel}";
            options.ResultCteName = cteName;

            var surrogateIdColumn = (options.ChainLevel == 0 || options.LastCteName == null) ? "ResourceSurrogateId" : "RefResourceSurrogateId";
            var typeIdColumn = (options.ChainLevel == 0 || options.LastCteName == null) ? "ResourceTypeId" : "RefResourceTypeId";

            sqlBuilder.BeginCte(cteName);
            sqlBuilder.SelectWithModifier("DISTINCT", $"r.{typeIdColumn}", $"r.{surrogateIdColumn}");
            sqlBuilder.From(options.LastCteName ?? "dbo.Resource", "r");

            var dateTime = DateTimeSqlParser.ParseValue(value, out var modifier);
            var minSurrogateId = ResourceSurrogateIdHelper.ToSurrogateId(dateTime.Start);
            var maxSurrogateId = ResourceSurrogateIdHelper.ToSurrogateId(dateTime.End.AddMilliseconds(1));

            // Because surrogate id is a range for the same datetime, different operators need to be handled accordingly.
            var whereClause = modifier switch
            {
                "gt" => $"r.ResourceSurrogateId >= {options.AddParameter(VLatest.Resource.ResourceSurrogateId, maxSurrogateId, includeInHash: true)}", // greater than means the start of the next millisecond, so max surrogate id is included
                "ge" => $"r.ResourceSurrogateId >= {options.AddParameter(VLatest.Resource.ResourceSurrogateId, minSurrogateId, includeInHash: true)}",
                "lt" => $"r.ResourceSurrogateId < {options.AddParameter(VLatest.Resource.ResourceSurrogateId, minSurrogateId, includeInHash: true)}",
                "le" => $"r.ResourceSurrogateId < {options.AddParameter(VLatest.Resource.ResourceSurrogateId, maxSurrogateId, includeInHash: true)}",
                "sa" => $"r.ResourceSurrogateId > {options.AddParameter(VLatest.Resource.ResourceSurrogateId, maxSurrogateId, includeInHash: true)}",
                "eb" => $"r.ResourceSurrogateId < {options.AddParameter(VLatest.Resource.ResourceSurrogateId, minSurrogateId, includeInHash: true)}",
                "ne" => $"(r.ResourceSurrogateId >= {options.AddParameter(VLatest.Resource.ResourceSurrogateId, maxSurrogateId, includeInHash: true)} OR r.ResourceSurrogateId < {options.AddParameter(VLatest.Resource.ResourceSurrogateId, minSurrogateId, includeInHash: true)})",
                "eq" => $"r.ResourceSurrogateId >= {options.AddParameter(VLatest.Resource.ResourceSurrogateId, minSurrogateId, includeInHash: true)} AND r.ResourceSurrogateId < {options.AddParameter(VLatest.Resource.ResourceSurrogateId, maxSurrogateId, includeInHash: true)}",
                _ => throw new ArgumentException($"Invalid operator '{modifier}' for lastUpdated search parameter."),
            };

            sqlBuilder.Where(whereClause);

            // Add base filters only on the first CTE
            ParserUtil.AddFirstCteFilters(sqlBuilder, options, "r");

            sqlBuilder.EndCte();
        }
    }
}
