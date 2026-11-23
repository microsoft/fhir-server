// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;

namespace Microsoft.Health.Fhir.Api.Features.Routing
{
    /// <summary>
    /// Route constants and helpers for partition-prefixed requests.
    /// </summary>
    internal static class PartitionRoute
    {
        internal const string PartitionId = "partitionId";
        internal const string TemplatePrefix = "partition/{partitionId}";

        internal static PathString GetFhirPath(HttpRequest request)
        {
            PathString prefix = GetPrefix(request);
            if (!prefix.HasValue)
            {
                return request.Path;
            }

            string path = request.Path.Value.Substring(prefix.Value.Length);
            return new PathString(path.Length == 0 ? "/" : path);
        }

        internal static PathString GetPrefix(HttpRequest request)
        {
            if (request.RouteValues.TryGetValue(PartitionId, out object partitionId))
            {
                // Use the matched route value, retaining the caller's casing of the literal prefix.
                return new PathString(request.Path.Value.Substring(0, "/partition/".Length + partitionId.ToString().Length));
            }

            return PathString.Empty;
        }
    }
}
