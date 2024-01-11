// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    /// <summary>
    /// Enum to specify the type of search to carry out.
    /// </summary>
    /// <remarks>
    /// With the exception of None, the flags attribute requires enumeration constants to be powers of two, that is, 1, 2, 4, 8.
    /// </remarks>
    [Flags]
    internal enum SqlSearchType
    {
        // Set if we do not need to consider history, deleted resources, or re-indexing
        Latest = 1,

        // Set if we are including previous resource versions without deleted resources
        History = 2,

        // Set if the search parameter hash value needs to be considered in a search
        Reindex = 4,

        // Set if we are including deleted resource versions
        SoftDeleted = 8,
    }
}
