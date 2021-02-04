// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Health.Fhir.Api.Features.Headers
{
    internal static class HeaderDictionaryExtensions
    {
        /// <summary>
        /// Adds x-ms-retry-after-ms and Retry-After response headers. The former is used by Cosmos DB and is expressed in ms, the latter is the W3C standard header and expressed in
        /// seconds. The seconds will be >= 1 unless the timespan is really 0.
        /// </summary>
        /// <param name="responseHeaders">The response headers</param>
        /// <param name="retryAfterTimeSpan">The time</param>
        public static void AddRetryAfterHeaders(this IHeaderDictionary responseHeaders, TimeSpan retryAfterTimeSpan)
        {
            responseHeaders.Add(
                KnownHeaders.RetryAfterMilliseconds,
                ((int)retryAfterTimeSpan.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));

            int retryAfterSeconds = retryAfterTimeSpan == default ? 0 : Math.Max(1, (int)Math.Round(retryAfterTimeSpan.TotalSeconds));

            responseHeaders.Add(
                KnownHeaders.RetryAfter,
                retryAfterSeconds.ToString(CultureInfo.InvariantCulture));
        }
    }
}
