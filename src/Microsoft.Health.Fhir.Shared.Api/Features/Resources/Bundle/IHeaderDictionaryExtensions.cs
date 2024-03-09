// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    internal static class IHeaderDictionaryExtensions
    {
        public static IHeaderDictionary Clone(this IHeaderDictionary headers)
        {
            EnsureArg.IsNotNull(headers, nameof(headers));

            var clone = new HeaderDictionary();
            foreach (var header in headers)
            {
                clone[header.Key] = header.Value;
            }

            return clone;
        }
    }
}
