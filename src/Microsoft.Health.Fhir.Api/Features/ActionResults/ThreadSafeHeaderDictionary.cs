// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Microsoft.Health.Fhir.Api.Features.ActionResults
{
    /// <summary>
    /// Represents a thread-safe wrapper for RequestHeaders and ResponseHeaders.
    /// </summary>
    internal class ThreadSafeHeaderDictionary : ConcurrentDictionary<string, StringValues>, IHeaderDictionary
    {
        public ThreadSafeHeaderDictionary()
            : base(StringComparer.OrdinalIgnoreCase)
        {
        }

        public ThreadSafeHeaderDictionary(IDictionary<string, StringValues> dictionary)
            : base(dictionary, StringComparer.OrdinalIgnoreCase)
        {
        }

        /// <inheritdoc />
        /// <remarks>Copy from https://github.com/dotnet/aspnetcore/blob/main/src/Http/Http/src/HeaderDictionary.cs</remarks>
        public long? ContentLength
        {
            get
            {
                long value;
                var rawValue = this[HeaderNames.ContentLength];
                if (rawValue.Count == 1 &&
                    !string.IsNullOrEmpty(rawValue[0]) &&
                    HeaderUtilities.TryParseNonNegativeInt64(new StringSegment(rawValue[0]).Trim(), out value))
                {
                    return value;
                }

                return null;
            }

            set
            {
                if (value.HasValue)
                {
                    this[HeaderNames.ContentLength] = HeaderUtilities.FormatNonNegativeInt64(value.GetValueOrDefault());
                }
                else
                {
                    TryRemove(HeaderNames.ContentLength, out StringValues values);
                }
            }
        }
    }
}
