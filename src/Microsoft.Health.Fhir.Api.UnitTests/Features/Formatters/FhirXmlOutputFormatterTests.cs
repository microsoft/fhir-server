// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Text;
using System.Xml;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Api.Features.Formatters;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Formatters
{
    public class FhirXmlOutputFormatterTests
    {
        [Fact]
        public void GivenAXmlElementAndXmlContentType_WhenCheckingCanWrite_ThenFalseShouldBeReturned()
        {
            bool result = CanWrite(typeof(XmlElement), ContentType.XML_CONTENT_HEADER);

            Assert.False(result);
        }

        [Fact]
        public void GivenAFhirObjectAndXmlContentType_WhenCheckingCanWrite_ThenTrueShouldBeReturned()
        {
            bool result = CanWrite(typeof(Observation), ContentType.XML_CONTENT_HEADER);

            Assert.True(result);
        }

        private bool CanWrite(Type modelType, string contentType)
        {
            var formatter = new FhirXmlOutputFormatter(new FhirXmlSerializer(), NullLogger<FhirXmlOutputFormatter>.Instance);

            var defaultHttpContext = new DefaultHttpContext();
            defaultHttpContext.Request.ContentType = contentType;

            var result = formatter.CanWriteResult(
                new OutputFormatterWriteContext(
                    new DefaultHttpContext(),
                    Substitute.For<Func<Stream, Encoding, TextWriter>>(),
                    modelType,
                    new Observation()));

            return result;
        }
    }
}
