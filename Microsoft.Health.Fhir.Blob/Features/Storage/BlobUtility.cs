// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;

namespace Microsoft.Health.Fhir.Blob.Features.Storage;

internal static class BlobUtility
{
    /// <summary>
    /// Gets a deterministic hash of length 3, for a given value
    /// </summary>
    /// <param name="value"> Value to be hashed</param>
    public static string ComputeHashPrefixForBlobName(long value)
    {
        EnsureArg.IsNotDefault(value, nameof(value));

        var hashCode = 0;
        foreach (var c in value.ToString())
        {
            hashCode = unchecked((hashCode * 251) + c);
        }

        return (Math.Abs(hashCode) % 512).ToString().PadLeft(3, '0');
    }
}
