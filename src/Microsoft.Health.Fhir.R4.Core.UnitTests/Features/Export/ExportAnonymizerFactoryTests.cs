// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.R4.Core.UnitTests.Features.Export
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
        public async Task GivenAValidAnonymizationConfiguration_WhenCreate_AnonymizerShouldBeCreated()
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
            string fileHash = "819BBD695AB6608688EBC0B91C988E60B02AC760D2010A863AE82452E9DFCE7E";

            ExportAnonymizerFactory factory = new ExportAnonymizerFactory(client, logger);
            IAnonymizer anonymizer = await factory.CreateAnonymizerAsync("http://dummy", fileHash, CancellationToken.None);

            Assert.NotNull(anonymizer);
        }

        [Fact]
        public async Task GivenAnInvalidAnonymizationConfiguration_WhenCreate_CorrectExceptionShouldBeThrow()
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
            _ = await Assert.ThrowsAsync<FailedToParseAnonymizationConfigurationException>(() => factory.CreateAnonymizerAsync("http://dummy", string.Empty, CancellationToken.None));
        }

        [Fact]
        public async Task GivenNoAnonymizationConfiguration_WhenCreate_CorrectExceptionShouldBeThrow()
        {
            IArtifactProvider client = Substitute.For<IArtifactProvider>();
            client.FetchAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns<Task>(
                x =>
                {
                    throw new FileNotFoundException();
                });

            ILogger<ExportJobTask> logger = Substitute.For<ILogger<ExportJobTask>>();

            ExportAnonymizerFactory factory = new ExportAnonymizerFactory(client, logger);
            _ = await Assert.ThrowsAsync<AnonymizationConfigurationNotFoundException>(() => factory.CreateAnonymizerAsync("http://dummy", string.Empty, CancellationToken.None));
        }

        [Fact]
        public async Task GivenInvalidFileHashValue_WhenCreate_CorrectExceptionShouldBeThrow()
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
            _ = await Assert.ThrowsAsync<AnonymizationConfigurationHashValueNotMatchException>(() => factory.CreateAnonymizerAsync("http://dummy", "InvalidValue", CancellationToken.None));
        }
    }
}
