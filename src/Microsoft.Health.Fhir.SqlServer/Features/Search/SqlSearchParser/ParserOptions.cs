// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    public class ParserOptions
    {
        public ContinuationToken? ContinuationToken { get; set; }

        public string CteName { get; set; } = string.Empty;

        public string? LastCteName { get; set; }

        public int ChainLevel { get; set; } = 0;

        public bool ParentIsForwardChain { get; set; } = false;

        public bool Sort { get; set; }

        public int Count { get; set; } = 10;

        public IList<short> ResourceTypes { get; init; } = new List<short>();

        public IList<short> ExcludedResourceTypes { get; init; } = new List<short>();

        public bool IncludeTotalCount { get; set; }

        public string? SortParameterName { get; set; }

        public bool SortDescending { get; set; }

        public bool SortIsSpecialParameter { get; set; }

        public bool SortQuerySecondPhase { get; set; }
    }
}
