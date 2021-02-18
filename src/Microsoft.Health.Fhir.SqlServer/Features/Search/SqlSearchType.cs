// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    [Flags]
    internal enum SqlSearchType
    {
        // The flags attribute requires enumeration constants to be powers of two, that is, 1, 2, 4, 8
        None = 0,
        History = 1,
        Reindex = 2,
    }
}
