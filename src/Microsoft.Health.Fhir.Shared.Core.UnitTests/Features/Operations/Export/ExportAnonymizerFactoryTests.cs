// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Anonymizer.Core;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Export
{
    public class ExportAnonymizerFactoryTests
    {
        private const string SampleConfiguration =
            @"
{
	""fhirPathRules"": [
		{
			""path"": ""Patient"",
			""method"": ""redact""
		}
	]
}";

        private const string InvalidConfiguration =
    @"
{
	""fhirPathRules"": [
		{
			""path"": ""invalid()"",
			""method"": ""redact""
		}
	]
}";

        [Fact]
        public void GivenAnonymizer_WhenUpdatingReferenceLibraryVersion_ThenAnonymizerShouldBeUpdatedToSameVersion()
        {
            AssemblyName[] referencesFromAnonymizationEngion = typeof(AnonymizerEngine).Assembly.GetReferencedAssemblies();
            Assert.Contains(typeof(FhirJsonParser).Assembly.FullName, referencesFromAnonymizationEngion.Select(r => r.FullName));
            Assert.Contains(typeof(JsonConvert).Assembly.FullName, referencesFromAnonymizationEngion.Select(r => r.FullName));
        }

        [Fact]
        public async Task GivenAValidAnonymizationConfiguration_WhenCreatingAnonymizer_AnonymizerShouldBeCreated()
        {
            IArtifactProvider client = Substitute.For<IArtifactProvider>();
            client.FetchAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns<Task>(
                x =>
                {
                    Stream target = x.ArgAt<Stream>(1);
                    target.Write(Encoding.UTF8.GetBytes(SampleConfiguration), 0, SampleConfiguration.Length);
                    return Task.CompletedTask;
                });

            ILogger<ExportJobTask> logger = Substitute.For<ILogger<ExportJobTask>>();

            ExportAnonymizerFactory factory = new ExportAnonymizerFactory(client, logger);
            IAnonymizer anonymizer = await factory.CreateAnonymizerAsync("http://dummy", CancellationToken.None);

            Assert.NotNull(anonymizer);
        }

        [Fact]
        public async Task GivenAnInvalidAnonymizationConfiguration_WhenCreatingAnonymizer_CorrectExceptionShouldBeThrow()
        {
            IArtifactProvider client = Substitute.For<IArtifactProvider>();
            client.FetchAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns<Task>(
                x =>
                {
                    Stream target = x.ArgAt<Stream>(1);
                    target.Write(Encoding.UTF8.GetBytes(InvalidConfiguration), 0, SampleConfiguration.Length);
                    return Task.CompletedTask;
                });

            ILogger<ExportJobTask> logger = Substitute.For<ILogger<ExportJobTask>>();

            ExportAnonymizerFactory factory = new ExportAnonymizerFactory(client, logger);
            _ = await Assert.ThrowsAsync<FailedToParseAnonymizationConfigurationException>(() => factory.CreateAnonymizerAsync("http://dummy", CancellationToken.None));
        }

        [Fact]
        public async Task GivenNoAnonymizationConfiguration_WhenCreatingAnonymizer_CorrectExceptionShouldBeThrow()
        {
            IArtifactProvider client = Substitute.For<IArtifactProvider>();
            client.FetchAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns<Task>(
                x =>
                {
                    throw new FileNotFoundException();
                });

            ILogger<ExportJobTask> logger = Substitute.For<ILogger<ExportJobTask>>();

            ExportAnonymizerFactory factory = new ExportAnonymizerFactory(client, logger);
            _ = await Assert.ThrowsAsync<AnonymizationConfigurationNotFoundException>(() => factory.CreateAnonymizerAsync("http://dummy", CancellationToken.None));
        }
    }
}
