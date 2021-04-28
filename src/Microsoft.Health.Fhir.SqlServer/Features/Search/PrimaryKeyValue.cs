// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    /// <summary>
    /// Represents a row in the Resource or search parameter tables by its primary key.
    /// </summary>
    internal record PrimaryKeyValue(short ResourceTypeId, long ResourceSurrogateId)
    {
        public override string ToString() => $"(PrimaryKey {ResourceTypeId} {ResourceSurrogateId})";
    }
}
