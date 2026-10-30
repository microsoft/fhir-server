// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Messages.Upsert
{
    /// <summary>
    /// How a conditional update is applied when its search finds no match and the body supplies an id.
    /// Other conditional update outcomes (no id, one match, multiple matches) are not affected.
    /// </summary>
    public enum ConditionalUpdateNoMatchBehavior
    {
        /// <summary>
        /// The resource is upserted by id, overwriting a live resource with that id. This is the default.
        /// </summary>
        Upsert = 0,

        /// <summary>
        /// The resource is only created; if a live resource with that id exists the request fails with 409 Conflict.
        /// </summary>
        Create = 1,
    }
}
