// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Health.Fhir.Ignixa;

/// <summary>
/// Extension methods for registering Ignixa serialization services with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Ignixa FHIR serialization services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method registers the following services:
    /// </para>
    /// <list type="bullet">
    /// <item><description><see cref="IIgnixaJsonSerializer"/> - JSON serialization service</description></item>
    /// <item><description><see cref="IgnixaFhirJsonInputFormatter"/> - ASP.NET Core input formatter</description></item>
    /// <item><description><see cref="IgnixaFhirJsonOutputFormatter"/> - ASP.NET Core output formatter</description></item>
    /// </list>
    /// <para>
    /// Note: This does NOT configure MVC to use these formatters. MVC formatter registration is
    /// mode-gated (<see cref="Microsoft.Health.Fhir.Core.Configs.FhirSdkMode"/>) and lives in
    /// <c>SdkModeFeatureModule</c>, not here — this method only makes the serializer and the concrete
    /// formatter singletons available for that module (and other consumers) to resolve.
    /// </para>
    /// <para>
    /// Important: This method requires that <see cref="FhirJsonParser"/> and <see cref="FhirJsonSerializer"/>
    /// are already registered in the service collection (typically done by FhirModule).
    /// The formatter resolves <see cref="IIgnixaSchemaContext"/> lazily during request processing.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddIgnixaSerialization(this IServiceCollection services)
    {
        EnsureArg.IsNotNull(services, nameof(services));

        // Register the Ignixa JSON serializer
        services.AddSingleton<IIgnixaJsonSerializer, IgnixaJsonSerializer>();

        // Register formatters - they depend on both Ignixa and Firely serializers for compatibility
        // The Firely serializers should already be registered by FhirModule
        // Note: The formatter resolves IIgnixaSchemaContext lazily during request processing
        // to avoid DI ordering issues during startup
        services.AddSingleton<IgnixaFhirJsonInputFormatter>(sp =>
        {
            var serializer = sp.GetRequiredService<IIgnixaJsonSerializer>();
            var parser = sp.GetRequiredService<FhirJsonParser>();

            // Pass the service provider - the formatter will resolve IIgnixaSchemaContext lazily
            return new IgnixaFhirJsonInputFormatter(serializer, parser, sp);
        });

        services.AddSingleton<IgnixaFhirJsonOutputFormatter>();

        return services;
    }

    /// <summary>
    /// Exposes the Ignixa JSON formatters (registered by <see cref="AddIgnixaSerialization"/>) as
    /// <see cref="TextInputFormatter"/>/<see cref="TextOutputFormatter"/> services, so MVC's single
    /// formatter-ordering owner (<c>FormatterConfiguration</c>) can see and order them.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// This lives here — not in <c>SdkModeFeatureModule</c> (Api layer) — because
    /// <see cref="IgnixaFhirJsonInputFormatter"/>/<see cref="IgnixaFhirJsonOutputFormatter"/> are
    /// <see langword="internal"/> and this assembly's <c>InternalsVisibleTo</c> does not extend to the
    /// production Api assemblies (only their UnitTests counterparts). Callers MUST only invoke this
    /// from a mode-gated branch (Ignixa or Hybrid) — calling it unconditionally would register Ignixa
    /// as a competing formatter even in Firely mode, reproducing the defect this seam exists to fix.
    /// </remarks>
    public static IServiceCollection AddIgnixaMvcFormatters(this IServiceCollection services)
    {
        EnsureArg.IsNotNull(services, nameof(services));

        services.AddSingleton<TextInputFormatter>(sp => sp.GetRequiredService<IgnixaFhirJsonInputFormatter>());
        services.AddSingleton<TextOutputFormatter>(sp => sp.GetRequiredService<IgnixaFhirJsonOutputFormatter>());

        return services;
    }
}
