// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    public static class CustomQueries
    {
        public static readonly Dictionary<string, string> QueryStore = new Dictionary<string, string>()
            {
                { "7100AADC5527EC3D808DB1A0F9F8C3B71522683D0F8EE1F0C1DA3E4570FE586B", "dbo.QueryIdentifierWithIncludeAndOtherParam"},
            };
    }
}
