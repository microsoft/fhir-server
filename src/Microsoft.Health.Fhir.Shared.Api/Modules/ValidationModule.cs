// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Features.Validation.Narratives;

namespace Microsoft.Health.Fhir.Api.Modules;

/// <summary>
/// Adds validators to container.
/// </summary>
public class ValidationModule : IStartupModule
{
    /// <inheritdoc />
    public void Load(IServiceCollection services)
    {
        EnsureArg.IsNotNull(services, nameof(services));

        // Adds basic FHIR model validation into MVC
        services.PostConfigure<MvcOptions>(options =>
        {
            // Removes default DataAnnotationsModelValidator
            options.ModelValidatorProviders.Clear();
        });

        services.TypesInSameAssembly(KnownAssemblies.All)
            .AssignableTo<IValidator>()
            .Singleton()
            .AsSelf()
            .AsImplementedInterfaces();

        services.AddSingleton<INarrativeHtmlSanitizer, NarrativeHtmlSanitizer>();

        // Register the Firely-based validator as a fallback for non-Ignixa resources
        services.AddSingleton<ModelAttributeValidator>();

        // Register the Ignixa-based validator as the primary IModelAttributeValidator
        // Uses fast-path validation (Tier 1-2) for Ignixa resources (~1-5ms)
        // Falls back to Firely DotNetAttributeValidation for non-Ignixa resources
        services.AddSingleton<IModelAttributeValidator>(sp =>
        {
            var schemaContext = sp.GetRequiredService<IIgnixaSchemaContext>();
            var fallbackValidator = sp.GetRequiredService<ModelAttributeValidator>();
            return new IgnixaResourceValidator(schemaContext, fallbackValidator);
        });

        services.AddSingleton<ServerProvideProfileValidation>();
        services.AddSingleton<ISupportedProfilesStore>(x => x.GetRequiredService<ServerProvideProfileValidation>());
        services.AddSingleton<IProvideProfilesForValidation>(x => x.GetRequiredService<ServerProvideProfileValidation>());

        services.AddSingleton<IProfileValidator, ProfileValidator>();
    }
}
