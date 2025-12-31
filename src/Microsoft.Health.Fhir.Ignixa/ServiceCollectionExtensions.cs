// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using EnsureThat;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
    /// </remarks>
    public static IServiceCollection AddIgnixaSerialization(this IServiceCollection services)
    {
        EnsureArg.IsNotNull(services, nameof(services));

        // Register the JSON serializer
        services.AddSingleton<IIgnixaJsonSerializer, IgnixaJsonSerializer>();

        // Register formatters (they can be resolved for manual configuration)
        services.AddSingleton<IgnixaFhirJsonInputFormatter>();
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
    /// </remarks>
    public static IServiceCollection AddIgnixaSerializationWithFormatters(this IServiceCollection services)
    {
        EnsureArg.IsNotNull(services, nameof(services));

        // Add base services
        services.AddIgnixaSerialization();

        // Configure MVC to use the formatters
        services.AddSingleton<IConfigureOptions<MvcOptions>, IgnixaFormatterConfiguration>();

        return services;
    }

    /// <summary>
    /// Configures MVC options to use Ignixa formatters.
    /// </summary>
    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated via dependency injection")]
    private sealed class IgnixaFormatterConfiguration : IConfigureOptions<MvcOptions>
    {
        private readonly IgnixaFhirJsonInputFormatter _inputFormatter;
        private readonly IgnixaFhirJsonOutputFormatter _outputFormatter;

        public IgnixaFormatterConfiguration(
            IgnixaFhirJsonInputFormatter inputFormatter,
            IgnixaFhirJsonOutputFormatter outputFormatter)
        {
            _inputFormatter = inputFormatter;
            _outputFormatter = outputFormatter;
        }

        public void Configure(MvcOptions options)
        {
            // Insert at the beginning to take precedence
            options.InputFormatters.Insert(0, _inputFormatter);
            options.OutputFormatters.Insert(0, _outputFormatter);
        }
    }
}
