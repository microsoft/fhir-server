// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Persistence;

public class RawResourceLocator
{
    public RawResourceLocator(long storageId, int offset)
    {
        RawResourceStorageIdentifier = storageId;
        RawResourceOffset = offset;
    }

    public long RawResourceStorageIdentifier { get; set; }

    public int RawResourceOffset { get; set; }

    public override bool Equals(object obj)
    {
        return obj is RawResourceLocator key &&
               RawResourceStorageIdentifier == key.RawResourceStorageIdentifier &&
               RawResourceOffset == key.RawResourceOffset;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(RawResourceStorageIdentifier, RawResourceOffset);
    }
}
