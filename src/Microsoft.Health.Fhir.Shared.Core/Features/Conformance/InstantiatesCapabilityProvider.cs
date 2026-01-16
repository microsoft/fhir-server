// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Conformance;

namespace Microsoft.Health.Fhir.Shared.Core.Features.Conformance
{
    /// <summary>
    /// Provides capability to populate the 'instantiates' field of the CapabilityStatement.
    /// </summary>
    public class InstantiatesCapabilityProvider : IVolatileProvideCapability
    {
        private readonly Func<IScoped<IEnumerable<IInstantiateCapability>>> _instantiateCapabilityDelegate;
        private readonly ILogger<InstantiatesCapabilityProvider> _logger;

        public InstantiatesCapabilityProvider(
            Func<IScoped<IEnumerable<IInstantiateCapability>>> instantiateCapabilityDelegate,
            ILogger<InstantiatesCapabilityProvider> logger)
        {
            EnsureArg.IsNotNull(instantiateCapabilityDelegate, nameof(instantiateCapabilityDelegate));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _instantiateCapabilityDelegate = instantiateCapabilityDelegate;
            _logger = logger;
        }

        public Task BuildAsync(ICapabilityStatementBuilder builder, CancellationToken cancellationToken)
            => PopulateInstantiatesAsync(builder, cancellationToken);

        public Task UpdateAsync(ICapabilityStatementBuilder builder, CancellationToken cancellationToken)
            => PopulateInstantiatesAsync(builder, cancellationToken);

        private async Task PopulateInstantiatesAsync(
            ICapabilityStatementBuilder builder,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            var urls = await GetUrlsAsync(cancellationToken);
            builder.Apply(
                x =>
                {
                    x.Instantiates = urls.Any() ? urls : null;
                });
        }

        private async Task<HashSet<string>> GetUrlsAsync(CancellationToken cancellationToken)
        {
            var capabilityUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var scopedCapabilities = _instantiateCapabilityDelegate())
            {
                var capabilities = scopedCapabilities.Value?.ToList() ?? new List<IInstantiateCapability>();
                foreach (var capability in capabilities)
                {
                    try
                    {
                        _logger.LogInformation("Getting canonical urls from '{Capability}'.", capability.GetType().Name);
                        var urls = await capability.GetCanonicalUrlsAsync(cancellationToken);

                        if (urls != null)
                        {
                            foreach (var url in urls)
                            {
                                capabilityUrls.Add(url);
                            }
                        }

                        _logger.LogInformation("{Count} canonical urls added.", urls?.Count ?? 0);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to get canonical urls from '{Capability}'.", capability.GetType().Name);
                    }
                }

                return capabilityUrls;
            }
        }
    }
}
