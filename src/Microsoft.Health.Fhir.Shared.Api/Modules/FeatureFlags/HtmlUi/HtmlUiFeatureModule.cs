// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Formatters;
using Microsoft.Health.Fhir.Api.Models;

namespace Microsoft.Health.Fhir.Api.Modules.FeatureFlags.HtmlUi
{
    public class HtmlUiFeatureModule : IStartupModule
    {
        private readonly FeatureConfiguration _featureConfiguration;

        public HtmlUiFeatureModule(FhirServerConfiguration fhirServerConfiguration)
        {
            EnsureArg.IsNotNull(fhirServerConfiguration, nameof(fhirServerConfiguration));
            _featureConfiguration = fhirServerConfiguration.Features;
        }

        /// <inheritdoc />
        public void Load(IServiceCollection services)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            // HTML
            // If UI is supported, then add the formatter so that the
            // document can be output in HTML view.
            if (_featureConfiguration.SupportsUI)
            {
                services.Add<HtmlOutputFormatter>()
                    .Singleton()
                    .AsSelf()
                    .AsService<TextOutputFormatter>();

                // Adds provider to serve embedded razor views
                services.Configure<MvcRazorRuntimeCompilationOptions>(options =>
                {
                    options.FileProviders.Add(new EmbeddedFileProvider(typeof(CodePreviewModel).Assembly));
                }).AddControllersWithViews().AddRazorRuntimeCompilation();
            }
        }
    }
}
