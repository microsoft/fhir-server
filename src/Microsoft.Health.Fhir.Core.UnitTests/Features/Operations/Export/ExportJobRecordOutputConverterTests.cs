// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using System.Reflection;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Export
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Export)]
    public class ExportJobRecordOutputConverterTests
    {
        [Fact]
        public void GivenExportJobRecordV1_WhenDeserialized_ThenOutputIsDeserializedCorrectly()
        {
            string data = GetDataFromFile("ExportJobRecordV1");
            ExportJobRecord jobRecord = JsonConvert.DeserializeObject<ExportJobRecord>(data);

            // Testing a few expected results
            Assert.True(jobRecord.Output.Count > 0);
            Assert.Single(jobRecord.Output["Claim"]);
            Assert.Single(jobRecord.Output["Patient"]);
            Assert.Equal("fcf87438-cdbd-4537-8f4d-4ab739310f3f", jobRecord.Id);
        }

        [Fact]
        public void GivenExportJobRecordV2_WhenDeserialized_ThenOutputIsDeserializedCorrectly()
        {
            string data = GetDataFromFile("ExportJobRecordV2");
            ExportJobRecord jobRecord = JsonConvert.DeserializeObject<ExportJobRecord>(data);

            // Testing a few expected results
            Assert.True(jobRecord.Output.Count > 0);
            Assert.Equal(3, jobRecord.Output["Claim"].Count);
            Assert.Equal(5, jobRecord.Output["ExplanationOfBenefit"].Count);
            Assert.Equal("058f7fbf-bc3a-448b-b0ff-9f21d514e177", jobRecord.Id);
        }

        private string GetDataFromFile(string fileName)
        {
            string resourceName = $"Microsoft.Health.Fhir.Core.UnitTests.TestFiles.{fileName}.json";
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
