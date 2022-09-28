// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Export
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.AnonymizedExport)]
    [Trait(Traits.Category, Categories.Export)]
    public class ExportAnonymizerTests
    {
        private static ExportJobRecord exportJobRecord = CreateDummyExportJobRecord();

        [Fact]
        public async Task GivenRedactAnonymizationConfig_WhenAnonymizeResource_ThenPropertiesShouldBeRedacted()
        {
            string configurationContent =
            @"
            {
	            ""fhirPathRules"": [
		            {
			            ""path"": ""Patient.name"",
			            ""method"": ""redact""
		            }
	            ]
            }";

            Patient patient = new Patient();
            patient.Name.Add(HumanName.ForFamily("Test"));
            IAnonymizer anonymizer = await CreateAnonymizerFromConfigContent(configurationContent);

            ResourceElement resourceElement = anonymizer.Anonymize(new ResourceElement(patient.ToTypedElement()));
            Patient anonymizedResource = resourceElement.Instance.ToPoco<Patient>();
            Assert.Empty(anonymizedResource.Name);
        }

        [Fact]
        public async Task GivenGeneralizeAnonymizationConfig_WhenAnonymizeResource_ThenPropertiesShouldBeGeneralized()
        {
            string configurationContent =
            @"
            {
              ""fhirPathRules"": [
                {
                  ""path"": ""Patient.birthDate"",
                  ""method"": ""generalize"",
                  ""cases"": {
                    ""$this <= @2020-01-01 and $this >= @1990-01-01"": ""@2000-01-01""
                  }
                }
              ]
            }";

            Patient patient = new Patient();
            patient.BirthDate = "2001-01-01";
            IAnonymizer anonymizer = await CreateAnonymizerFromConfigContent(configurationContent);

            ResourceElement resourceElement = anonymizer.Anonymize(new ResourceElement(patient.ToTypedElement()));
            Patient anonymizedResource = resourceElement.Instance.ToPoco<Patient>();
            Assert.Equal("2000-01-01", anonymizedResource.BirthDate);
        }

        [Fact]
        public async Task GivenCryptoHashAnonymizationConfig_WhenAnonymizeResource_ThenHashedNodeShouldBeReturned()
        {
            string configurationContent =
            @"
            {
              ""fhirPathRules"": [
                {
                  ""path"": ""Resource.id"",
                  ""method"": ""cryptoHash""
                }
              ],
              ""parameters"": {
                ""cryptoHashKey"": ""123""
              }
            }";

            Patient patient = new Patient();
            patient.Id = "123";
            IAnonymizer anonymizer = await CreateAnonymizerFromConfigContent(configurationContent);

            ResourceElement resourceElement = anonymizer.Anonymize(new ResourceElement(patient.ToTypedElement()));
            Patient anonymizedResource = resourceElement.Instance.ToPoco<Patient>();
            Assert.Equal("3cafe40f92be6ac77d2792b4b267c2da11e3f3087b93bb19c6c5133786984b44", anonymizedResource.Id);
        }

        [Fact]
        public async Task GivenDateShiftAnonymizationConfig_WhenAnonymizeResource_ThenShiftedNodeShouldBeReturned()
        {
            string configurationContent =
            @"
            {
              ""fhirPathRules"": [
                {
                  ""path"": ""Patient.birthDate"",
                  ""method"": ""dateShift""
                }
              ],
              ""parameters"": {
                ""dateShiftKey"": ""123""
              }
            }";

            Patient patient = new Patient();
            patient.BirthDate = "2001-01-01";
            IAnonymizer anonymizer = await CreateAnonymizerFromConfigContent(configurationContent);

            ResourceElement resourceElement = anonymizer.Anonymize(new ResourceElement(patient.ToTypedElement()));
            Patient anonymizedResource = resourceElement.Instance.ToPoco<Patient>();
            Assert.Equal("2001-02-20", anonymizedResource.BirthDate);
        }

        [Fact]
        public async Task GivenSubstituteAnonymizationConfig_WhenAnonymizeResource_ThenSubstitutedNodeShouldBeReturned()
        {
            string configurationContent =
            @"
            {
              ""fhirPathRules"": [
                {
                  ""path"": ""Patient.name.family"",
                  ""method"": ""substitute"",
                  ""replaceWith"": ""test""
                }
              ]
            }";

            Patient patient = new Patient();
            patient.Name.Add(HumanName.ForFamily("input"));
            IAnonymizer anonymizer = await CreateAnonymizerFromConfigContent(configurationContent);

            ResourceElement resourceElement = anonymizer.Anonymize(new ResourceElement(patient.ToTypedElement()));
            Patient anonymizedResource = resourceElement.Instance.ToPoco<Patient>();
            Assert.Equal("test", anonymizedResource.Name.First().Family);
        }

        [Fact]
        public async Task GivenPerturbAnonymizationConfig_WhenAnonymizeResource_ThenPerturbedNodeShouldBeReturned()
        {
            string configurationContent =
            @"
            {
              ""fhirPathRules"": [
                {
                  ""path"": ""Condition.onset as Age"",
                  ""method"": ""perturb"",
                  ""span"": 0,
                  ""roundTo"": ""2""
                }
              ]
            }";

            Condition condition = new Condition();
            condition.Onset = new Age { Value = 20 };
            IAnonymizer anonymizer = await CreateAnonymizerFromConfigContent(configurationContent);

            ResourceElement resourceElement = anonymizer.Anonymize(new ResourceElement(condition.ToTypedElement()));
            Condition anonymizedResource = resourceElement.Instance.ToPoco<Condition>();
            Assert.InRange((anonymizedResource.Onset as Age).Value.GetValueOrDefault(), 20, 20);
        }

        private async Task<IAnonymizer> CreateAnonymizerFromConfigContent(string configuration)
        {
            IArtifactProvider client = Substitute.For<IArtifactProvider>();
            client.FetchAsync(Arg.Any<ExportJobRecord>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns<Task>(
                x =>
                {
                    Stream target = x.ArgAt<Stream>(1);
                    target.Write(Encoding.UTF8.GetBytes(configuration), 0, configuration.Length);
                    return Task.CompletedTask;
                });

            ILogger<ExportJobTask> logger = Substitute.For<ILogger<ExportJobTask>>();

            ExportAnonymizerFactory factory = new ExportAnonymizerFactory(client, logger);
            return await factory.CreateAnonymizerAsync(exportJobRecord, CancellationToken.None);
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
