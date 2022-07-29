// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Net.Http.Headers;
using System.Text;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Specification.Source;
using Hl7.Fhir.Specification.Terminology;
using Hl7.Fhir.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Terminology;
using Microsoft.Health.Fhir.Core.Features.Validation;

namespace Microsoft.Health.Fhir.Api.Modules
{
    public class TerminologyModule : IStartupModule
    {
        public void Load(IServiceCollection services)
        {
            FhirClient client = null;
            ZipSource zipSource = null;
            ServiceProvider serviceProvider = services.BuildServiceProvider();
            ILogger<TerminologyModule> terminologyLogger = serviceProvider.GetService<ILogger<TerminologyModule>>();

            try
            {
                zipSource = ZipSource.CreateValidationSource(@"definitions.zip");
            }
            catch (FileNotFoundException ex)
            {
                terminologyLogger.LogCritical(ex, "FHIR Definitions folder not found");
                throw;
            }

            // Force summaries to load
            zipSource.ListSummaries();

            Func<IServiceProvider, ExternalTerminologyService> externalTSResolver = (service) =>
            {
                ExternalTerminologyService externalTerminologyService = null;
                try
                {
                    IOptions<ValidateOperationConfiguration> options = service.GetRequiredService<IOptions<ValidateOperationConfiguration>>();

                    if (!string.IsNullOrEmpty(options.Value.ExternalTerminologyServer))
                    {
                        var settings = new FhirClientSettings
                        {
                            Timeout = 300000,
                            PreferredFormat = ResourceFormat.Json,
                            VerifyFhirVersion = true,
                            PreferredReturn = Prefer.ReturnRepresentation,
                        };

                        if (!string.IsNullOrEmpty(options.Value.ApiKey))
                        {
                            var handler = new AuthorizationMessageHandler();
                            string encodedKey = Convert.ToBase64String(ASCIIEncoding.ASCII.GetBytes("apikey" + ":" + options.Value.ApiKey));
                            handler.Authorization = new AuthenticationHeaderValue("Basic", encodedKey);
                            client = new FhirClient(options.Value.ExternalTerminologyServer, settings, handler);
                        }
                        else
                        {
                            client = new FhirClient(options.Value.ExternalTerminologyServer, settings);
                        }

                        externalTerminologyService = new ExternalTerminologyService(client);
                    }
                }
                catch (Exception ex)
                {
                    terminologyLogger.LogCritical(ex, "Failed to create External Terminology Service");
                    throw;
                }

                return externalTerminologyService;
            };

            Func<IServiceProvider, FallbackTerminologyService> tsResolver = service =>
            {
                ExternalTerminologyService externalTerminologyService = null;
                FallbackTerminologyService ts = null;
                try
                {
                    IProvideProfilesForValidation profilesResolver = service.GetRequiredService<IProvideProfilesForValidation>();
                    IOptions<ValidateOperationConfiguration> options = service.GetRequiredService<IOptions<ValidateOperationConfiguration>>();
                    var resolver = new MultiResolver(new CachedResolver(zipSource, options.Value.CacheDurationInSeconds), profilesResolver);

                    if (!string.IsNullOrEmpty(options.Value.ProfileValidationTerminologyServer))
                    {
                        var settings = new FhirClientSettings
                        {
                            Timeout = 300000,
                            PreferredFormat = ResourceFormat.Json,
                            VerifyFhirVersion = true,
                            PreferredReturn = Prefer.ReturnRepresentation,
                        };

                        if (!string.IsNullOrEmpty(options.Value.ApiKey))
                        {
                            var handler = new AuthorizationMessageHandler();
                            string encodedKey = Convert.ToBase64String(ASCIIEncoding.ASCII.GetBytes("apikey" + ":" + options.Value.ApiKey));
                            handler.Authorization = new AuthenticationHeaderValue("Basic", encodedKey);
                            client = new FhirClient(options.Value.ProfileValidationTerminologyServer, settings, handler);
                        }
                        else
                        {
                            client = new FhirClient(options.Value.ProfileValidationTerminologyServer, settings);
                        }

                        externalTerminologyService = new ExternalTerminologyService(client);

                        // might want to return a Local terminology service in the future when there is one.
                        ts = new FallbackTerminologyService(new LocalTerminologyService(resolver.AsAsync(), new ValueSetExpanderSettings() { ValueSetSource = resolver }), externalTerminologyService);
                    }
                }
                catch (Exception ex)
                {
                    terminologyLogger.LogCritical(ex, "Failed to create Fallback Terminology Service");
                    throw;
                }

                return ts;
            };

            Func<IServiceProvider, Validator> validatorResolver = service =>
            {
                IProvideProfilesForValidation profilesResolver = service.GetRequiredService<IProvideProfilesForValidation>();
                IOptions<ValidateOperationConfiguration> options = service.GetRequiredService<IOptions<ValidateOperationConfiguration>>();
                var resolver = new MultiResolver(new CachedResolver(zipSource, options.Value.CacheDurationInSeconds), profilesResolver);

                var ctx = new ValidationSettings()
                {
                    ResourceResolver = resolver,
                    GenerateSnapshot = true,
                    Trace = false,
                    ResolveExternalReferences = false,
                    TerminologyService = service.GetRequiredService<FallbackTerminologyService>(),
                };

                return new Validator(ctx);
            };

            services.AddSingleton<FallbackTerminologyService>(tsResolver);
            services.AddSingleton<ExternalTerminologyService>(externalTSResolver);
            services.AddSingleton(validatorResolver);
            services.AddSingleton<ITerminologyOperator, TerminologyOperator>();
        }
    }
}
