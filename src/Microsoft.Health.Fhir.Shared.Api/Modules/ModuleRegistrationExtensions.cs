// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Modules.FeatureFlags.HtmlUi;
using Microsoft.Health.Fhir.Api.Modules.FeatureFlags.Validate;
using Microsoft.Health.Fhir.Api.Modules.FeatureFlags.XmlFormatter;

namespace Microsoft.Health.Fhir.Api.Modules
{
    public static class ModuleRegistrationExtensions
    {
        public static IServiceCollection RegisterModules(
            this IServiceCollection services,
            FhirServerConfiguration configuration,
            KnownModuleNames modules = KnownModuleNames.All)
        {
            if ((modules & KnownModuleNames.Anonymization) == KnownModuleNames.Anonymization)
            {
#if R4 && !R4B
                services.RegisterModule<AnonymizationModule>(configuration);
#endif
            }

            if ((modules & KnownModuleNames.Mediation) == KnownModuleNames.Mediation)
            {
                services.RegisterModule<MediationModule>(configuration);
            }

            if ((modules & KnownModuleNames.Mvc) == KnownModuleNames.Mvc)
            {
                services.RegisterModule<MvcModule>(configuration);
                services.RegisterModule<HtmlUiFeatureModule>(configuration);
                services.RegisterModule<ValidateFeatureModule>(configuration);
                services.RegisterModule<XmlFormatterFeatureModule>(configuration);
            }

            if ((modules & KnownModuleNames.Operations) == KnownModuleNames.Operations)
            {
                services.RegisterModule<OperationsModule>(configuration);
            }

            if ((modules & KnownModuleNames.Persistence) == KnownModuleNames.Persistence)
            {
                services.RegisterModule<PersistenceModule>(configuration);
            }

            if ((modules & KnownModuleNames.Search) == KnownModuleNames.Search)
            {
                services.RegisterModule<SearchModule>(configuration);
            }

            if ((modules & KnownModuleNames.Validation) == KnownModuleNames.Validation)
            {
                services.RegisterModule<OperationsModule>(configuration);
            }

            return services;
        }
    }
}
