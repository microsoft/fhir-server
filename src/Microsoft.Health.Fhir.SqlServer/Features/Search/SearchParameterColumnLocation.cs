// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    /// <summary>
    /// Indicates where the column(s) of a search parameter are located.
    /// </summary>
    [Flags]
    internal enum SearchParameterColumnLocation
    {
        ResourceTable = 1,
        SearchParamTable = 1 << 1,
    }
}
