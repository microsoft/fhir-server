// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.SqlServer.Registration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Registration
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class TemporalSearchConfigurationTests
    {
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void GivenScalarAndClassificationFlags_WhenConfigurationResolved_ThenWarnsOnlyIfClassificationIsMissing(
            bool scalar,
            bool classification)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
            {
                ["FhirSqlServer:EnableScalarTemporalEqualityRewriter"] = scalar.ToString(),
            }).Build());
            services.Configure<CoreFeatureConfiguration>(options => options.EnableSearchParameterTemporalClassification = classification);
            var logger = Substitute.For<ILogger<FhirSqlServerConfiguration>>();
            services.AddSingleton(logger);
            var builder = Substitute.For<IFhirServerBuilder>();
            builder.Services.Returns(services);
            builder.AddSqlServer(_ => { });
            using var provider = services.BuildServiceProvider();

            var configuration = provider.GetRequiredService<FhirSqlServerConfiguration>();

            Assert.Equal(scalar, configuration.EnableScalarTemporalEqualityRewriter);
            Assert.False(configuration.EnableInstantDateTimeRewriter);
            Assert.False(configuration.EnableFhirDateContainment);
            var warnings = logger.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log)
                && (LogLevel)call.GetArguments()[0] == LogLevel.Warning).ToList();
            if (scalar && !classification)
            {
                Assert.Contains("EnableScalarTemporalEqualityRewriter", Assert.Single(warnings).GetArguments()[2].ToString());
            }
            else
            {
                Assert.Empty(warnings);
            }
        }
    }
}
