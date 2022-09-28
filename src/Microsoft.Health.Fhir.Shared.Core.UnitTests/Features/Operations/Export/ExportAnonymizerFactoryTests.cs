// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Export
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.AnonymizedExport)]
    [Trait(Traits.Category, Categories.Export)]
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

        private static ExportJobRecord exportJobRecord = CreateDummyExportJobRecord();

        [Fact]
        public async Task GivenAValidAnonymizationConfiguration_WhenCreatingAnonymizer_AnonymizerShouldBeCreated()
        {
            IArtifactProvider client = Substitute.For<IArtifactProvider>();
            client.FetchAsync(Arg.Any<ExportJobRecord>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns<Task>(
                x =>
                {
                    Stream target = x.ArgAt<Stream>(1);
                    target.Write(Encoding.UTF8.GetBytes(SampleConfiguration), 0, SampleConfiguration.Length);
                    return Task.CompletedTask;
                });

            ILogger<ExportJobTask> logger = Substitute.For<ILogger<ExportJobTask>>();

            ExportAnonymizerFactory factory = new ExportAnonymizerFactory(client, logger);
            IAnonymizer anonymizer = await factory.CreateAnonymizerAsync(exportJobRecord, CancellationToken.None);

            Assert.NotNull(anonymizer);
        }

        [Fact]
        public async Task GivenAnInvalidAnonymizationConfiguration_WhenCreatingAnonymizer_CorrectExceptionShouldBeThrow()
        {
            IArtifactProvider client = Substitute.For<IArtifactProvider>();
            client.FetchAsync(Arg.Any<ExportJobRecord>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns<Task>(
                x =>
                {
                    Stream target = x.ArgAt<Stream>(1);
                    target.Write(Encoding.UTF8.GetBytes(InvalidConfiguration), 0, SampleConfiguration.Length);
                    return Task.CompletedTask;
                });

            ILogger<ExportJobTask> logger = Substitute.For<ILogger<ExportJobTask>>();

            ExportAnonymizerFactory factory = new ExportAnonymizerFactory(client, logger);
            _ = await Assert.ThrowsAsync<FailedToParseAnonymizationConfigurationException>(() => factory.CreateAnonymizerAsync(exportJobRecord, CancellationToken.None));
        }

        [Fact]
        public async Task GivenNoAnonymizationConfiguration_WhenCreatingAnonymizer_CorrectExceptionShouldBeThrow()
        {
            IArtifactProvider client = Substitute.For<IArtifactProvider>();
            client.FetchAsync(Arg.Any<ExportJobRecord>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns<Task>(
                x =>
                {
                    throw new FileNotFoundException();
                });

            ILogger<ExportJobTask> logger = Substitute.For<ILogger<ExportJobTask>>();

            ExportAnonymizerFactory factory = new ExportAnonymizerFactory(client, logger);
            _ = await Assert.ThrowsAsync<AnonymizationConfigurationNotFoundException>(() => factory.CreateAnonymizerAsync(exportJobRecord, CancellationToken.None));
        }

        private static ExportJobRecord CreateDummyExportJobRecord()
        {
            return new ExportJobRecord(
                new Uri("http://localhost/dummy/"),
                ExportJobType.Patient,
                ExportFormatTags.ResourceName,
                resourceType: null,
                filters: null,
                hash: "123",
                rollingFileSizeInMB: 64,
                anonymizationConfigurationLocation: "dummy",
                requestorClaims: null)
            {
                Status = OperationStatus.Queued,
            };
        }
    }
}
