// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    public class ParserOptions
    {
        public ContinuationToken? ContinuationToken { get; set; }

        public IncludesContinuationToken? IncludesContinuationToken { get; set; }

        public int CteNumber { get; set; } = 0;

        public string? LastCteName { get; set; }

        public int ChainLevel { get; set; } = 0;

        public bool ParentIsForwardChain { get; set; } = false;

        public bool Sort { get; set; }

        public int Count { get; set; } = 10;

        public int IncludeCount { get; set; } = 10;

        public IList<short> ResourceTypes { get; init; } = new List<short>();

        public IList<short> ExcludedResourceTypes { get; init; } = new List<short>();

        public bool GetTotalCount { get; set; }

        public string? SortParameterName { get; set; }

        public bool SortDescending { get; set; }

        public bool SortIsSpecialParameter { get; set; }

        public bool SortQuerySecondPhase { get; set; }

        public string SortContinuationToken { get; set; } = string.Empty;

        public long? SortContinuationResourceSurrogateId { get; set; }

        public bool IsIterateInclude { get; set; }

        /// <summary>
        /// Gets or sets the name of the result CTE produced by the parser.
        /// Set by chain/reverse-chain parsers so callers know which CTE to reference.
        /// </summary>
        public string? ResultCteName { get; set; }

        public ResourceVersionType ResourceVersionType { get; set; } = ResourceVersionType.Latest;

        public SqlQueryBuilder SqlQueryBuilder { get; set; } = new SqlQueryBuilder();
    }
}
