// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Health.Fhir.Api.Features.Formatters;
using Microsoft.Health.Fhir.Api.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Shared.Core.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;
using static Hl7.Fhir.Model.Bundle;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Formatters
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class FhirJsonOutputFormatterTests
    {
        private static readonly Observation[] Resources = new[]
        {
            new Observation()
            {
                Id = Guid.NewGuid().ToString(),
            },
            new Observation()
            {
                Id = Guid.NewGuid().ToString(),
            },
        };

        private static readonly FhirJsonParser Parser = new FhirJsonParser();

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

        [Theory]
        [InlineData(false, null)]
        [InlineData(false, "?_elements=identifier,status")]
        [InlineData(false, "?_summary=text")]
        [InlineData(true, null)]
        [InlineData(true, "?_elements=identifier,status")]
        [InlineData(true, "?_summary=text")]
        public async Task GivenContext_WhenWritingResponseBody_ThenBundleShouldBeWrittenCorrectly(
            bool raw,
            string query)
        {
            await Run(true, raw, query);
        }

        [Theory]
        [InlineData(false, null)]
        [InlineData(false, "?_elements=identifier,status")]
        [InlineData(false, "?_summary=text")]
        [InlineData(true, null)]
        [InlineData(true, "?_elements=identifier,status")]
        [InlineData(true, "?_summary=text")]
        public async Task GivenContext_WhenWritingResponseBody_ThenResourceShouldBeWrittenCorrectly(
            bool raw,
            string query)
        {
            await Run(false, raw, query);
        }

        private static async Task Run(
            bool bundle,
            bool raw,
            string query)
        {
            using var writer = new StringWriter(new StringBuilder());
            using var body = new MemoryStream();

            var httpContext = new DefaultHttpContext();
            httpContext.Request.QueryString = new QueryString(query);
            httpContext.Response.StatusCode = (int)HttpStatusCode.OK;
            httpContext.Response.Body = body;

            var parameters = ParseQuery(query);
            var elements = parameters
                .Where(x => string.Equals(x.Item1, KnownQueryParameterNames.Elements, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var summary = httpContext.GetSummaryTypeOrDefault();
            var @object = CreateObject(
                bundle,
                raw);
            var objectType = bundle ? typeof(Hl7.Fhir.Model.Bundle) : (raw ? typeof(RawResourceElement) : typeof(Resource));
            var writeContext = new OutputFormatterWriteContext(
                httpContext,
                (_, _) => writer,
                objectType,
                @object);
            var formatter = new FhirJsonOutputFormatter(
               new FhirJsonSerializer(),
               Deserializers.ResourceDeserializer,
               ArrayPool<char>.Shared,
               new BundleSerializer(),
               ModelInfoProvider.Instance);
            await formatter.WriteResponseBodyAsync(writeContext, Encoding.UTF8);

            var content = default(string);
            if (elements.Any() || summary != SummaryType.False || (!bundle && !raw) || (bundle && !raw))
            {
                content = writer.ToString();
            }
            else
            {
                body.Seek(0, SeekOrigin.Begin);
                using (var reader = new StreamReader(body))
                {
                    content = await reader.ReadToEndAsync();
                }
            }

            Validate(
                bundle,
                raw,
                elements.Any(),
                summary,
                content);
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

        private static object CreateObject(bool bundle, bool raw)
        {
            var wrapper = new ResourceWrapper(
                Resources[0].ToResourceElement(),
                new RawResource(Resources[0].ToJson(), FhirResourceFormat.Json, false),
                null,
                false,
                null,
                null,
                null);
            if (bundle)
            {
                var r = new Hl7.Fhir.Model.Bundle()
                {
                    Type = BundleType.Batch,
                };

                r.Entry.Add(new RawBundleEntryComponent(wrapper));
                if (!raw)
                {
                    r.Entry.Add(new EntryComponent() { Resource = Resources[1] });
                }

                return r;
            }
            else if (raw)
            {
                return new RawResourceElement(wrapper);
            }

            return Resources[0];
        }

        private static List<Tuple<string, string>> ParseQuery(string query)
        {
            var result = new List<Tuple<string, string>>();
            if (!string.IsNullOrEmpty(query))
            {
                var parameters = HttpUtility.ParseQueryString(query);
                foreach (var k in parameters.AllKeys)
                {
                    foreach (var p in parameters.GetValues(k))
                    {
                        result.Add(Tuple.Create(k, p));
                    }
                }
            }

            return result;
        }

        private static void Validate(
            bool bundle,
            bool raw,
            bool hasElements,
            SummaryType summaryType,
            string content)
        {
            Assert.False(string.IsNullOrEmpty(content));

            var r = Parser.Parse<Resource>(content);
            Assert.NotNull(r);
            if (bundle)
            {
                Assert.IsType<Hl7.Fhir.Model.Bundle>(r);
                var b = (Hl7.Fhir.Model.Bundle)r;

                var expected = new List<Observation>();
                if (!raw)
                {
                    expected.AddRange(Resources);
                }
                else
                {
                    expected.Add(Resources[0]);
                }

                Assert.Equal(expected.Count, b.Entry.Count);
                foreach (var o in b.Entry.Select(x => x.Resource))
                {
                    if (hasElements || summaryType != SummaryType.False)
                    {
                        Assert.NotEmpty(o.Meta.Tag);
                    }
                    else
                    {
                        Assert.Equal(0, o.Meta?.Tag.Count ?? 0);
                    }
                }
            }
            else
            {
                Assert.IsType(Resources[0].GetType(), r);

                var o = (Observation)r;
                if (hasElements || summaryType != SummaryType.False)
                {
                    Assert.NotEmpty(o.Meta.Tag);
                }
                else
                {
                    Assert.Equal(0, o.Meta?.Tag.Count ?? 0);
                }
            }
        }
    }
}
