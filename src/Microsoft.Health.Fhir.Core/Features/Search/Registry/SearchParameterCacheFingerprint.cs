// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Definition;

namespace Microsoft.Health.Fhir.Core.Features.Search.Registry
{
    /// <summary>
    /// Identifies the actual definitions and search states held by an instance's SearchParameter cache.
    /// </summary>
    public static class SearchParameterCacheFingerprint
    {
        /// <summary>
        /// The event process used for package-install cache convergence.
        /// </summary>
        public const string UpdateProcessName = "FhirPackageCacheUpdate";

        /// <summary>
        /// Computes an order-independent fingerprint of the local definitions and per-resource hashes.
        /// </summary>
        /// <param name="definitions">The local definition manager.</param>
        /// <returns>A SHA-256 fingerprint.</returns>
        public static string Calculate(ISearchParameterDefinitionManager definitions)
        {
            EnsureArg.IsNotNull(definitions, nameof(definitions));

            var snapshot = new
            {
                Definitions = definitions.AllSearchParameters
                    .OrderBy(p => p.Url?.OriginalString, StringComparer.Ordinal)
                    .Select(p => new
                    {
                        Url = p.Url?.OriginalString,
                        p.Name,
                        p.Code,
                        p.Type,
                        p.Expression,
                        p.DefinitionStatus,
                        Base = p.BaseResourceTypes?.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                        Target = p.TargetResourceTypes?.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                        Components = p.Component?.Select(c => new { Url = c.DefinitionUrl?.OriginalString, c.Expression }).ToArray(),
                        p.VectorConfig,
                        p.TemporalType,
                        p.SearchParameterStatus,
                        p.IsSearchable,
                        p.IsSupported,
                        p.IsPartiallySupported,
                        p.SortStatus,
                        p.IsSystemDefined,
                    })
                    .ToArray(),
                ResourceTypeHashes = definitions.SearchParameterHashMap.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray(),
            };

            return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(snapshot)));
        }
    }
}
