// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Registration;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Extension methods for registering Ignixa persistence services with the FHIR server.
    /// </summary>
    public static class FhirServerBuilderIgnixaPersistenceRegistrationExtensions
    {
        /// <summary>
        /// Adds Ignixa-based persistence components to the FHIR server.
        /// </summary>
        /// <param name="fhirServerBuilder">The FHIR server builder.</param>
        /// <returns>The FHIR server builder for chaining.</returns>
        /// <remarks>
        /// <para>
        /// This method registers the following Ignixa persistence services:
        /// </para>
        /// <list type="bullet">
        /// <item><description><see cref="IIgnixaRawResourceFactory"/> - Creates RawResource from ResourceJsonNode</description></item>
        /// <item><description><see cref="IIgnixaResourceDeserializer"/> - Deserializes to IgnixaResourceElement</description></item>
        /// </list>
        /// <para>
        /// Note: <see cref="IIgnixaSchemaContext"/> and Ignixa serialization services are registered
        /// in FhirModule and must be available before this method is called.
        /// </para>
        /// <para>
        /// These services complement the existing Firely-based persistence components and can be
        /// used alongside them during migration.
        /// </para>
        /// </remarks>
        public static IFhirServerBuilder AddIgnixaPersistence(this IFhirServerBuilder fhirServerBuilder)
        {
            EnsureArg.IsNotNull(fhirServerBuilder, nameof(fhirServerBuilder));

            var services = fhirServerBuilder.Services;

            // Note: IIgnixaSchemaContext and Ignixa serialization services are registered in FhirModule
            // before this method is called. This method only registers persistence-specific components.

            // Register raw resource factory (singleton - stateless)
            services.AddSingleton<IIgnixaRawResourceFactory, IgnixaRawResourceFactory>();

            // Register resource deserializer (singleton - stateless)
            services.AddSingleton<IIgnixaResourceDeserializer, IgnixaResourceDeserializer>();

            return fhirServerBuilder;
        }
    }
}
