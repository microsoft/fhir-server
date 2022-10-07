// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Buffers;
using System.IO;
using System.Text;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Health.Fhir.Api.Features.Formatters;
using Microsoft.Health.Fhir.Api.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Formatters
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class FhirJsonOutputFormatterTests
    {
        [Fact]
        public void GivenAJObjectAndJsonContentType_WhenCheckingCanWrite_ThenFalseShouldBeReturned()
        {
            bool result = CanRead(typeof(JObject), ContentType.JSON_CONTENT_HEADER);

            Assert.False(result);
        }

        [Fact]
        public void GivenAFhirObjectAndJsonContentType_WhenCheckingCanWrite_ThenTrueShouldBeReturned()
        {
            bool result = CanRead(typeof(Observation), ContentType.JSON_CONTENT_HEADER);

            Assert.True(result);
        }

        [Fact]
        public void GivenAResourceWrapperJsonContentType_WhenCheckingCanWrite_ThenTrueShouldBeReturned()
        {
            bool result = CanRead(typeof(RawResourceElement), ContentType.JSON_CONTENT_HEADER);

            Assert.True(result);
        }

        private bool CanRead(Type modelType, string contentType)
        {
            var formatter = new FhirJsonOutputFormatter(
               new FhirJsonSerializer(),
               Deserializers.ResourceDeserializer,
               ArrayPool<char>.Shared,
               new BundleSerializer(),
               ModelInfoProvider.Instance);

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
