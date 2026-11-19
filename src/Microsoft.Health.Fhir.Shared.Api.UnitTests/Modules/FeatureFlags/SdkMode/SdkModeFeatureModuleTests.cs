// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Formatters;
using Microsoft.Health.Fhir.Api.Modules.FeatureFlags.SdkMode;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Modules.FeatureFlags.SdkMode
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class SdkModeFeatureModuleTests
    {
        // The Ignixa formatters are internal to the Core-layer assembly and are not named directly here;
        // identity is asserted by runtime type name after resolving them through real DI.
        private const string IgnixaInputFormatterTypeName = "IgnixaFhirJsonInputFormatter";
        private const string IgnixaOutputFormatterTypeName = "IgnixaFhirJsonOutputFormatter";

        [Fact]
        public void GivenIgnixaMode_WhenLoaded_ThenOnlyIgnixaJsonFormattersAreRegistered()
        {
            (IReadOnlyList<TextInputFormatter> inputs, IReadOnlyList<TextOutputFormatter> outputs) = ResolveFormatters(FhirSdkMode.Ignixa);

            Assert.Equal(IgnixaInputFormatterTypeName, Assert.Single(inputs).GetType().Name);
            Assert.Equal(IgnixaOutputFormatterTypeName, Assert.Single(outputs).GetType().Name);
        }

        [Fact]
        public void GivenFirelyMode_WhenLoaded_ThenOnlyFirelyJsonFormattersAreRegistered()
        {
            (IReadOnlyList<TextInputFormatter> inputs, IReadOnlyList<TextOutputFormatter> outputs) = ResolveFormatters(FhirSdkMode.Firely);

            Assert.IsType<FhirJsonInputFormatter>(Assert.Single(inputs));
            Assert.IsType<FhirJsonOutputFormatter>(Assert.Single(outputs));
        }

        [Fact]
        public void GivenHybridMode_WhenLoaded_ThenIgnixaFormattersAreRegisteredBeforeFirely()
        {
            (IReadOnlyList<TextInputFormatter> inputs, IReadOnlyList<TextOutputFormatter> outputs) = ResolveFormatters(FhirSdkMode.Hybrid);

            Assert.Collection(
                inputs,
                f => Assert.Equal(IgnixaInputFormatterTypeName, f.GetType().Name),
                f => Assert.IsType<FhirJsonInputFormatter>(f));

            Assert.Collection(
                outputs,
                f => Assert.Equal(IgnixaOutputFormatterTypeName, f.GetType().Name),
                f => Assert.IsType<FhirJsonOutputFormatter>(f));
        }

        [Fact]
        public void GivenNullConfiguration_WhenConstructed_ThenArgumentNullExceptionIsThrown()
        {
            Assert.Throws<ArgumentNullException>(() => new SdkModeFeatureModule(null));
        }

        private static (IReadOnlyList<TextInputFormatter> Inputs, IReadOnlyList<TextOutputFormatter> Outputs) ResolveFormatters(FhirSdkMode mode)
        {
            var services = new ServiceCollection();

            // Prerequisites shared by both the Firely and Ignixa formatter constructors.
            services.AddSingleton(new FhirJsonParser());
            services.AddSingleton(new FhirJsonSerializer());
            services.AddSingleton(ArrayPool<char>.Shared);
            services.AddSingleton(Deserializers.ResourceDeserializer);
            services.AddSingleton(new BundleSerializer(new FhirJsonSerializer()));
            services.AddSingleton<IModelInfoProvider>(ModelInfoProvider.Instance);

            // Registers the concrete Ignixa formatter singletons + IIgnixaJsonSerializer so the
            // mode-gated TextInputFormatter/TextOutputFormatter forwarders have something to resolve.
            services.AddIgnixaSerialization();

            var fhirServerConfiguration = new FhirServerConfiguration();
            fhirServerConfiguration.CoreFeatures.SdkMode = mode;

            new SdkModeFeatureModule(fhirServerConfiguration).Load(services);

            using ServiceProvider provider = services.BuildServiceProvider();

            return (
                provider.GetServices<TextInputFormatter>().ToList(),
                provider.GetServices<TextOutputFormatter>().ToList());
        }
    }
}
