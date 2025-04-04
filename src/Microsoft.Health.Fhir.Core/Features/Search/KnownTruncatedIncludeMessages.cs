// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    internal class KnownTruncatedIncludeMessages
    {
        public static bool IsKnownMessage(string message) => string.Equals(message, Core.Resources.TruncatedIncludeMessage, StringComparison.OrdinalIgnoreCase)
            || string.Equals(message, Core.Resources.TruncatedIncludeMessageForIncludes, StringComparison.OrdinalIgnoreCase);
    }
}
