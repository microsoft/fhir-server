// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    public static class SqlSearchParameters
    {
        public const string ResourceSurrogateIdParameterName = "_resourceSurrogateId";

        public static readonly SearchParameterInfo ResourceSurrogateIdParameter = new SearchParameterInfo(ResourceSurrogateIdParameterName);
    }
}
