// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using EnsureThat;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Modules.FeatureFlags.ConditionalUpdates
{
    public class ConditionalUpsertPostConfigureOptions : IPostConfigureOptions<MvcOptions>
    {
        private readonly IConfiguredConformanceProvider _configuredConformanceProvider;

        public ConditionalUpsertPostConfigureOptions(
            IConfiguredConformanceProvider configuredConformanceProvider)
        {
            EnsureArg.IsNotNull(configuredConformanceProvider, nameof(configuredConformanceProvider));

            _configuredConformanceProvider = configuredConformanceProvider;
        }

        public void PostConfigure(string name, MvcOptions options)
        {
            // Turns on conditional updates, even when not configured in the DefaultCapabilities.json
            // If this is not enabled in the capability statement
            // ConditionalUpsertResourceRequest will fail with a MethodNotAllowedException

            _configuredConformanceProvider
                .ConfigureOptionalCapabilities(x =>
                {
                    foreach (ListedResourceComponent r in x.Rest.Server().Resource)
                    {
                        if (r.Interaction.Any(y => string.Equals(y.Code, TypeRestfulInteraction.Update, StringComparison.Ordinal)))
                        {
                            r.ConditionalUpdate = true;
                        }
                    }
                });
        }
    }
}
