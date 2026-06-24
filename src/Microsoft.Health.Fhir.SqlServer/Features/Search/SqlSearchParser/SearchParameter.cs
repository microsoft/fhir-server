// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    public class SearchParameter
    {
        public required string Code { get; init; }

        public required IReadOnlyList<string> ResourceTypes { get; init; }

        public required IReadOnlyList<string> TargetResourceTypes { get; init; }

        public required string Type { get; init; }

        public required int Id { get; init; }
    }
}
