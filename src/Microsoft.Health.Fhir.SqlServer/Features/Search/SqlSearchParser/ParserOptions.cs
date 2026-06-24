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

        public string? LastCteName { get; set; }

        public int ChainLevel { get; set; }

        public bool Sort { get; set; }

        public int Count { get; set; } = 10;

        public IList<int> ResourceTypes { get; init; } = new List<int>();

        public bool IncludeTotalCount { get; set; }

        public string? SortParameterName { get; set; }

        public bool SortDescending { get; set; }

        public bool SortIsSpecialParameter { get; set; }

        public bool SortQuerySecondPhase { get; set; }
    }
}
