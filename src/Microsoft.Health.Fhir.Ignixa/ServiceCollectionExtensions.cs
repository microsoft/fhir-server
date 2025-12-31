// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics.CodeAnalysis;
using EnsureThat;
using Hl7.Fhir.Serialization;
using Ignixa.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Features.Search.FhirPath;
using Microsoft.Health.Fhir.Ignixa.FhirPath;

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
    /// Note: This does NOT automatically configure MVC to use these formatters.
    /// Use <see cref="AddIgnixaSerializationWithFormatters"/> to also configure MVC options.
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
    /// Adds Ignixa FHIR serialization services and configures MVC to use the formatters.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method registers all Ignixa services and configures MVC options to include
    /// the Ignixa input and output formatters. The formatters are inserted at the beginning
    /// of the formatter lists to take precedence over existing formatters.
    /// </para>
    /// <para>
    /// Use this method when you want Ignixa formatters to handle FHIR JSON requests/responses
    /// instead of the existing Firely-based formatters.
    /// </para>
    /// <para>
    /// The Ignixa formatters support both Ignixa types (<see cref="Ignixa.Serialization.SourceNodes.ResourceJsonNode"/>,
    /// <see cref="IgnixaResourceElement"/>) and Firely types (<see cref="Hl7.Fhir.Model.Resource"/>,
    /// <see cref="Microsoft.Health.Fhir.Core.Models.RawResourceElement"/>) for gradual migration.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddIgnixaSerializationWithFormatters(this IServiceCollection services)
    {
        EnsureArg.IsNotNull(services, nameof(services));

        // Add base services
        services.AddIgnixaSerialization();

        // Configure MVC to use the formatters (inserted at the beginning for precedence)
        services.AddSingleton<IConfigureOptions<MvcOptions>, IgnixaFormatterConfiguration>();

        return services;
    }

    /// <summary>
    /// Adds Ignixa FHIRPath provider to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="schemaResolver">A function that resolves the ISchema from the service provider.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method registers <see cref="IgnixaFhirPathProvider"/> as the <see cref="IFhirPathProvider"/>,
    /// replacing the default Firely-based provider. The Ignixa provider offers:
    /// </para>
    /// <list type="bullet">
    /// <item><description>Delegate compilation for ~80% of common search patterns</description></item>
    /// <item><description>Native IElement evaluation without conversion overhead</description></item>
    /// <item><description>Full FHIRPath 2.0 specification support</description></item>
    /// <item><description>Expression caching for performance</description></item>
    /// </list>
    /// </remarks>
    public static IServiceCollection AddIgnixaFhirPath(
        this IServiceCollection services,
        Func<IServiceProvider, ISchema> schemaResolver)
    {
        EnsureArg.IsNotNull(services, nameof(services));
        EnsureArg.IsNotNull(schemaResolver, nameof(schemaResolver));

        // Register the Ignixa FHIRPath provider, replacing any existing registration
        services.RemoveAll<IFhirPathProvider>();
        services.AddSingleton<IFhirPathProvider>(provider =>
        {
            var schema = schemaResolver(provider);
            return new IgnixaFhirPathProvider(schema);
        });

        return services;
    }

    /// <summary>
    /// Configures MVC options to use Ignixa formatters.
    /// </summary>
    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated via dependency injection")]
    private sealed class IgnixaFormatterConfiguration : IConfigureOptions<MvcOptions>
    {
        private readonly IServiceProvider _serviceProvider;

        public IgnixaFormatterConfiguration(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public void Configure(MvcOptions options)
        {
            // Resolve formatters from service provider to ensure all dependencies are available
            var inputFormatter = _serviceProvider.GetRequiredService<IgnixaFhirJsonInputFormatter>();
            var outputFormatter = _serviceProvider.GetRequiredService<IgnixaFhirJsonOutputFormatter>();

            // Insert at the beginning to take precedence over Firely formatters
            options.InputFormatters.Insert(0, inputFormatter);
            options.OutputFormatters.Insert(0, outputFormatter);
        }
    }
}
