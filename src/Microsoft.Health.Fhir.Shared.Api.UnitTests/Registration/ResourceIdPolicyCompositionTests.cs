// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Resources.Create;
using Microsoft.Health.Fhir.Core.Features.Resources.Upsert;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
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

        [Theory]
        [InlineData("false", 64, true)]
        [InlineData("false", 65, false)]
        [InlineData("true", 128, true)]
        [InlineData("true", 129, false)]
        public void GivenConfiguration_WhenResolvingRegisteredValidators_ThenTheConfiguredLimitIsEnforced(string setting, int idLength, bool expectedValid)
        {
            using ServiceProvider provider = BuildProvider(setting);
            var resource = new Hl7.Fhir.Model.Patient { Id = new string('a', idLength) };

            IValidator<CreateResourceRequest> create = Assert.Single(provider.GetServices<IValidator<CreateResourceRequest>>());
            IValidator<UpsertResourceRequest> upsert = Assert.Single(provider.GetServices<IValidator<UpsertResourceRequest>>());

            Assert.Equal(expectedValid, !HasIdError(create.Validate(new CreateResourceRequest(resource.ToResourceElement()))));
            Assert.Equal(expectedValid, !HasIdError(upsert.Validate(new UpsertResourceRequest(resource.ToResourceElement()))));
        }

        private static bool HasIdError(FluentValidation.Results.ValidationResult result)
            => result.Errors.Any(e => e.ErrorCode == "IdValidator");

        private static ServiceProvider BuildProvider(string setting)
        {
            var values = new Dictionary<string, string>();
            if (setting != null)
            {
                values[SettingKey] = setting;
            }

            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(NSubstitute.Substitute.For<Microsoft.Extensions.Hosting.IHostApplicationLifetime>());
            services.AddFhirServer(configuration);
            ServiceProvider provider = services.BuildServiceProvider();

            var requestContext = NSubstitute.Substitute.For<Microsoft.Health.Fhir.Core.Features.Context.IFhirRequestContext>();
            requestContext.RequestHeaders.Returns(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>());
            provider.GetRequiredService<Microsoft.Health.Core.Features.Context.RequestContextAccessor<Microsoft.Health.Fhir.Core.Features.Context.IFhirRequestContext>>().RequestContext = requestContext;
            return provider;
        }
    }
}
