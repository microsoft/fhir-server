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
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Conformance;

namespace Microsoft.Health.Fhir.Shared.Core.Features.Conformance
{
    public class InstantiatesCapabilityProvider : IProvideCapability
    {
        private readonly Func<IScoped<IEnumerable<IInstantiateCapability>>> _instantiateCapabilityDelegate;

        public InstantiatesCapabilityProvider(
            Func<IScoped<IEnumerable<IInstantiateCapability>>> instantiateCapabilityDelegate)
        {
            EnsureArg.IsNotNull(instantiateCapabilityDelegate, nameof(instantiateCapabilityDelegate));

            _instantiateCapabilityDelegate = instantiateCapabilityDelegate;
        }

        public Task BuildAsync(
            ICapabilityStatementBuilder builder,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            var instantiateUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var instantiates = _instantiateCapabilityDelegate();
            foreach (var instantiate in instantiates.Value)
            {
                if (instantiate.TryGetUrls(out var urls) && urls != null && urls.Any())
                {
                    foreach (var url in urls)
                    {
                        instantiateUrls.Add(url);
                    }
                }
            }

            if (instantiateUrls.Any())
            {
                builder.Apply(
                    x =>
                    {
                        x.Instantiates = instantiateUrls;
                    });
            }

            return Task.CompletedTask;
        }
    }
}
