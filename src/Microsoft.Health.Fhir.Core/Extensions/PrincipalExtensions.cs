// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Security.Claims;

namespace Microsoft.Health.Fhir.Core.Extensions;

internal static class PrincipalExtensions
{
    public static string ToBase64(this ClaimsPrincipal principal)
    {
        using var memoryStream = new MemoryStream();
        using var writer = new BinaryWriter(memoryStream);
        principal.WriteTo(writer);
        writer.Flush();
        var serializedPrincipal = Convert.ToBase64String(memoryStream.ToArray());
        return serializedPrincipal;
    }
}
