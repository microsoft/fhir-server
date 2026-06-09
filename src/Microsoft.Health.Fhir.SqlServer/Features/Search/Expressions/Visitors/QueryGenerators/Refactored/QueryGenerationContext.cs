// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Features.Schema;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators.Refactored
{
    /// <summary>
    /// Encapsulates all state needed during SQL query generation.
    /// Replaces scattered field-based state management with a cohesive context object.
    /// </summary>
    internal class QueryGenerationContext
    {
        public QueryGenerationContext(
            IndentedStringBuilder stringBuilder,
            HashingSqlQueryParameterManager parameters,
            ISqlServerFhirModel model,
            SchemaInformation schemaInfo,
            SearchOptions searchOptions,
            bool reuseQueryPlans,
            bool isAsyncOperation)
        {
            StringBuilder = stringBuilder;
            Parameters = parameters;
            Model = model;
            SchemaInfo = schemaInfo;
            SearchOptions = searchOptions;
            ReuseQueryPlans = reuseQueryPlans;
            IsAsyncOperation = isAsyncOperation;
            SearchParamIds = new HashSet<short>();
            CteToLimit = new HashSet<int>();
        }

        public IndentedStringBuilder StringBuilder { get; }

        public HashingSqlQueryParameterManager Parameters { get; }

        public ISqlServerFhirModel Model { get; }

        public SchemaInformation SchemaInfo { get; }

        public SearchOptions SearchOptions { get; set; }

        public bool ReuseQueryPlans { get; }

        public bool IsAsyncOperation { get; }

        public HashSet<short> SearchParamIds { get; }

        // CTE (Common Table Expression) tracking
        public int CurrentCteIndex { get; set; } = -1;

        public int UnionAggregateCteIndex { get; set; } = -1;

        public int SmartV2ScopeUnionCteIndex { get; set; } = -1;

        public HashSet<int> CteToLimit { get; }

        public string MainSelectCte { get; set; }

        public List<string> IncludeCteIds { get; set; }

        public Dictionary<string, List<string>> IncludeLimitCtesByResourceType { get; set; }

        public List<string> IncludeFromCteIds { get; set; }

        // Visitor state tracking
        public bool SortVisited { get; set; }

        public bool UnionVisited { get; set; }

        public bool SmartV2UnionVisited { get; set; }

        public bool FirstChainAfterUnionVisited { get; set; }

        public bool HasIdentifier { get; set; }

        public int SearchParamCount { get; set; }

        public int StackDepth { get; set; }

        public SqlRootExpression RootExpression { get; set; }

        public bool PreviousSqlQueryGeneratorFailure { get; set; }

        public int MaxTableExpressionCountLimitForExists { get; set; } = 5;

        public string GetNextCteTableName()
        {
            CurrentCteIndex++;
            return $"cte{CurrentCteIndex}";
        }

        public string GetCurrentCteTableName() => $"cte{CurrentCteIndex}";

        public static string GetCteTableName(int index) => $"cte{index}";
    }
}
