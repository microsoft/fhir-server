// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;

namespace Microsoft.Health.Fhir.Api.Extensions
{
    public static class HttpStatusCodeExtensions
    {
        public static string ToStatusCodeClass(this HttpStatusCode input)
        {
            return $"{(int)input / 100}xx";
        }
    }
}
