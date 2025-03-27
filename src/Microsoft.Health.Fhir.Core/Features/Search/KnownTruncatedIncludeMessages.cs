// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    internal class KnownTruncatedIncludeMessages
    {
        public static readonly HashSet<string> Messages = new HashSet<string>(
            new string[]
            {
                Core.Resources.TruncatedIncludeMessage,
                Core.Resources.TruncatedIncludeMessageForIncludes,
            },
            StringComparer.OrdinalIgnoreCase);
    }
}
