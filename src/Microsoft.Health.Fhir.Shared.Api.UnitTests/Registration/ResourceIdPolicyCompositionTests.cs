// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Registration
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class ResourceIdPolicyCompositionTests
    {
        private const string SettingKey = "FhirServer:CoreFeatures:UseLongResourceIds";

        [Theory]
        [InlineData(null, 64)]
        [InlineData("false", 64)]
        [InlineData("true", 128)]
        public void GivenConfiguration_WhenBuildingTheServerGraph_ThenTheRegisteredPolicyMatches(string setting, int expectedLength)
        {
            using ServiceProvider provider = BuildProvider(setting);

            Assert.Equal(expectedLength, provider.GetRequiredService<ResourceIdPolicy>().MaxLength);
        }

        private static ServiceProvider BuildProvider(string setting)
        {
            var values = new Dictionary<string, string>();
            if (setting != null)
            {
                values[SettingKey] = setting;
            }

            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            var services = new ServiceCollection();
            services.AddFhirServer(configuration);
            return services.BuildServiceProvider();
        }
    }
}
