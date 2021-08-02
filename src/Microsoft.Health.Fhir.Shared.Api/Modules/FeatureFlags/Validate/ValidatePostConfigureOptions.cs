// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Modules.FeatureFlags.Validate
{
    public class ValidatePostConfigureOptions : IPostConfigureOptions<MvcOptions>
    {
        private readonly IConfiguredConformanceProvider _configuredConformanceProvider;
        private readonly IModelInfoProvider _modelInfoProvider;

        public ValidatePostConfigureOptions(
            IConfiguredConformanceProvider configuredConformanceProvider,
            IModelInfoProvider modelInfoProvider)
        {
            EnsureArg.IsNotNull(configuredConformanceProvider, nameof(configuredConformanceProvider));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));

            _configuredConformanceProvider = configuredConformanceProvider;
            _modelInfoProvider = modelInfoProvider;
        }

        public void PostConfigure(string name, MvcOptions options)
        {
            _configuredConformanceProvider
                .ConfigureOptionalCapabilities(x =>
                {
                    x.Rest.Server().Operation.Add(new OperationComponent
                    {
                        Name = OperationTypes.Validate,
                        Definition = new ReferenceComponent
                        {
                            Reference = OperationTypes.ValidateUri,
                        },
                    });
                });
        }
    }
}
