// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public static class ConformanceProviderExtensions
    {
        public static async Task<bool> CanKeepHistory(this IConformanceProvider conformanceProvider, string resourceType, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await conformanceProvider.SatisfiesAsync(
                new[]
                {
                    new CapabilityQuery(
                        $"CapabilityStatement.rest.resource.where(type = '{resourceType}').where(versioning = 'versioned-update').exists()" +
                        $"or CapabilityStatement.rest.resource.where(type = '{resourceType}').where(versioning = 'versioned').exists()"),
                },
                cancellationToken);
        }

        public static async Task<bool> RequireETag(this IConformanceProvider conformanceProvider, string resourceType, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await conformanceProvider.SatisfiesAsync(
                new[]
                {
                    new CapabilityQuery($"CapabilityStatement.rest.resource.where(type = '{resourceType}').where(versioning = 'versioned-update').exists()"),
                },
                cancellationToken);
        }

        public static async Task<bool> CanUpdateCreate(this IConformanceProvider conformanceProvider, string resourceType, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await conformanceProvider.SatisfiesAsync(
                new[]
                {
                    new CapabilityQuery($"CapabilityStatement.rest.resource.where(type = '{resourceType}').updateCreate = true"),
                },
                cancellationToken);
        }

        public static async Task<bool> CanReadHistory(this IConformanceProvider conformanceProvider, string resourceType, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await conformanceProvider.SatisfiesAsync(
                new[]
                {
                    new CapabilityQuery($"CapabilityStatement.rest.resource.where(type = '{resourceType}').readHistory"),
                },
                cancellationToken);
        }
    }
}
