// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    public static class SearchOptionsExtensions
    {
        public static int GetOffset(this SearchOptions searchOptions)
        {
            return int.TryParse(searchOptions.ContinuationToken, out var offset) ? Math.Abs(offset) : 0;
        }
    }
}
