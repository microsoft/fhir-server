// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using static Hl7.Fhir.Model.CapabilityStatement;

namespace Microsoft.Health.Fhir.Api.Modules.FeatureFlags.Bundles
{
    public class BundlePostConfigureOptions : IPostConfigureOptions<MvcOptions>
    {
        private readonly CoreFeatureConfiguration _coreFeatures;
        private readonly IConfiguredConformanceProvider _configuredConformanceProvider;

        public BundlePostConfigureOptions(IOptions<CoreFeatureConfiguration> coreFeatures, IConfiguredConformanceProvider configuredConformanceProvider)
        {
            EnsureArg.IsNotNull(coreFeatures, nameof(coreFeatures));
            EnsureArg.IsNotNull(configuredConformanceProvider, nameof(configuredConformanceProvider));

            _coreFeatures = coreFeatures.Value;
            _configuredConformanceProvider = configuredConformanceProvider;
        }

        public void PostConfigure(string name, MvcOptions options)
        {
            if (_coreFeatures.SupportsBatch)
            {
                // Turns on batch, even when not configured in the DefaultCapabilities.json
                // If this is not enabled in the capability statement
                // PostBundleRequest for Batch will fail with a MethodNotAllowedException

                _configuredConformanceProvider
                    .ConfigureOptionalCapabilities(x =>
                    {
                        x.Rest.First().Interaction.Add(new CapabilityStatement.SystemInteractionComponent
                        {
                            Code = SystemRestfulInteraction.Batch,
                        });
                    });
            }

            // _supportTransaction flag should always be turned off if CosmosDb is choosen as a persistence layer.
            if (_coreFeatures.SupportsTransaction)
            {
                // Turns on transaction, even when not configured in the DefaultCapabilities.json
                // If this is not enabled in the capability statement
                // PostBundleRequest for Transaction will fail with a MethodNotAllowedException

                _configuredConformanceProvider
                    .ConfigureOptionalCapabilities(x =>
                    {
                        x.Rest.First().Interaction.Add(new CapabilityStatement.SystemInteractionComponent
                        {
                            Code = SystemRestfulInteraction.Transaction,
                        });
                    });
            }
        }
    }
}
