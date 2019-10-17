// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Models;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Conformance
{
    public class ConformanceTests
    {
        [Fact]
        public void ConformanceBuilder()
        {
            string httpMicrosoftCom = "http://microsoft.com";

            var builder = CapabilityStatementBuilder.Create(FhirSpecification.R4);
            builder.Update(x => x.Url = new System.Uri(httpMicrosoftCom));

            var statement = builder.Build();

            object url = statement.Scalar("Resource.url");

            Assert.Equal(httpMicrosoftCom, url);
        }
    }
}
