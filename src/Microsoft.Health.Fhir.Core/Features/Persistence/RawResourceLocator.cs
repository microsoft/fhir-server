// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Persistence;

public class RawResourceLocator
{
    public RawResourceLocator(long storageId, int offset, int resourceLength)
    {
        RawResourceStorageIdentifier = storageId;
        RawResourceOffset = offset;
        RawResourceLength = resourceLength;
    }

    public long RawResourceStorageIdentifier { get; set; }

    public int RawResourceOffset { get; set; }

    public int RawResourceLength { get; set; }

    public override bool Equals(object obj)
    {
        return obj != null && obj.GetType() == GetType() &&
               RawResourceStorageIdentifier == ((RawResourceLocator)obj).RawResourceStorageIdentifier &&
               RawResourceOffset == ((RawResourceLocator)obj).RawResourceOffset;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(RawResourceStorageIdentifier, RawResourceOffset);
    }
}
