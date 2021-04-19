// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
#pragma warning disable CA2227 // Collection properties should be read only
    public record KeySetValue(short CurrentPositionResourceTypeId, long CurrentPositionResourceSurrogateId, BitArray NextResourceTypeIds);
#pragma warning restore CA2227 // Collection properties should be read only
}
