// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class DateTimeOffsetExtensions
    {
        private const string DateTimeOffsetFormat = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK";

        public static string ToInstantString(this DateTimeOffset offset)
        {
            return offset.ToString(DateTimeOffsetFormat);
        }
    }
}
