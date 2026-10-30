// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Fhir.Api.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Compartment;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Shared.Core.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;
using static Hl7.Fhir.Model.Bundle;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Api.UnitTests.Features.Resources.Bundle
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Bundle)]
    public class BundleSerializerTests
    {
        private readonly ResourceWrapperFactory _wrapperFactory;
        private readonly BundleSerializer _bundleSerializer = new BundleSerializer(new FhirJsonSerializer());

        public BundleSerializerTests()
        {
            var requestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            requestContextAccessor.RequestContext.Returns(x => new FhirRequestContext("get", "https://localhost/Patient", "https://localhost", "correlation", new Dictionary<string, StringValues>(), new Dictionary<string, StringValues>()));

            _wrapperFactory = new ResourceWrapperFactory(
                                     new RawResourceFactory(new FhirJsonSerializer()),
                                     requestContextAccessor,
                                     Substitute.For<ISearchIndexer>(),
                                     Substitute.For<IClaimsExtractor>(),
                                     Substitute.For<ICompartmentIndexer>(),
                                     Substitute.For<ISearchParameterDefinitionManager>(),
                                     Deserializers.ResourceDeserializer);
        }

        [Fact]
        public async Task GivenBundleWithNoEntry_WhenSerialized_ShouldMatchSerializationByBuiltInSerializer()
        {
            var (rawBundle, bundle) = CreateBundle();

            await Validate(rawBundle, bundle);
        }

        [Fact]
        public async Task GivenBundleWithOneEntry_WhenSerialized_MatchesSerializationByBuiltInSerializer()
        {
            var patientResource = Samples.GetDefaultPatient();

            var (rawBundle, bundle) = CreateBundle(patientResource);

            await Validate(rawBundle, bundle);
        }

        [Fact]
        public async Task GivenBundleWithMultipleEntries_WhenSerialized_MatchesSerializationByBuiltInSerializer()
        {
            var patientResource = Samples.GetDefaultPatient();
            var observationResource = Samples.GetDefaultObservation().ToPoco();
            var organizationResource = Samples.GetDefaultOrganization().ToPoco();

            observationResource.Id = Guid.NewGuid().ToString();
            organizationResource.Id = Guid.NewGuid().ToString();

            var (rawBundle, bundle) = CreateBundle(patientResource, observationResource.ToResourceElement(), organizationResource.ToResourceElement());

            await Validate(rawBundle, bundle);
        }

        [Fact]
        public async Task GivenBundleWithLinks_WhenSerialized_ThenLinksShouldBeSerializedSuccessfully()
        {
            var patientResource = Samples.GetDefaultPatient();

            var (rawBundle, _) = CreateBundle(patientResource);
            var url = "https://localhost/Patient";
            rawBundle.SelfLink = new Uri(url);
            rawBundle.NextLink = new Uri($"{url}/next");
            rawBundle.PreviousLink = new Uri($"{url}/previous");
            rawBundle.FirstLink = new Uri($"{url}/first");
            rawBundle.LastLink = new Uri($"{url}/last");

            var parser = new FhirJsonParser(DefaultParserSettings.Settings);
            using (var stream = new MemoryStream())
            {
                await _bundleSerializer.Serialize(rawBundle, stream);
                stream.Seek(0, SeekOrigin.Begin);
                using (var reader = new StreamReader(stream))
                {
                    var serialized = await reader.ReadToEndAsync();
                    var actual = parser.Parse<Hl7.Fhir.Model.Bundle>(serialized);
                    Assert.Equal(rawBundle.SelfLink.ToString(), actual.SelfLink?.ToString());
                    Assert.Equal(rawBundle.NextLink.ToString(), actual.NextLink?.ToString());
                    Assert.Equal(rawBundle.PreviousLink.ToString(), actual.PreviousLink?.ToString());
                    Assert.Equal(rawBundle.FirstLink.ToString(), actual.FirstLink?.ToString());
                    Assert.Equal(rawBundle.LastLink.ToString(), actual.LastLink?.ToString());
                }
            }
        }

        [Fact]
        public async Task GivenBundleWithMetadata_WhenSerialized_ThenMetadataShouldBeSerializedSuccessfully()
        {
            var patientResource = Samples.GetDefaultPatient();

            var (rawBundle, _) = CreateBundle(patientResource);
            rawBundle.Meta = new Hl7.Fhir.Model.Meta
            {
                LastUpdated = DateTimeOffset.UtcNow,
            };

            var parser = new FhirJsonParser(DefaultParserSettings.Settings);
            using (var stream = new MemoryStream())
            {
                await _bundleSerializer.Serialize(rawBundle, stream);
                stream.Seek(0, SeekOrigin.Begin);
                using (var reader = new StreamReader(stream))
                {
                    var serialized = await reader.ReadToEndAsync();
                    var actual = parser.Parse<Hl7.Fhir.Model.Bundle>(serialized);
                    Assert.Equal(rawBundle.Meta.LastUpdated, actual.Meta?.LastUpdated);
                }
            }
        }

        public static IEnumerable<object[]> SearchMetadataCases()
        {
            string[] searches =
            {
                null,
                "{}",
                "{\"mode\":\"match\"}",
                "{\"score\":0.91}",
                "{\"score\":0}",
                "{\"mode\":\"match\",\"score\":0.91}",
                "{\"mode\":\"match\",\"score\":0}",
                "{\"id\":\"search-id\"}",
                "{\"extension\":[{\"url\":\"http://example.org/search\",\"extension\":[{\"url\":\"label\",\"valueString\":\"quoted \\\"value\\\"\"},{\"url\":\"weight\",\"valueDecimal\":0.25}]}]}",
                "{\"modifierExtension\":[{\"url\":\"http://example.org/modifier\",\"valueBoolean\":true}]}",
                "{\"mode\":\"include\",\"_mode\":{\"id\":\"mode-id\",\"extension\":[{\"url\":\"http://example.org/mode\",\"valueString\":\"details\"}]},\"score\":0.91,\"_score\":{\"id\":\"score-id\",\"extension\":[{\"url\":\"http://example.org/score\",\"valueDecimal\":0.25}]}}",
                "{\"_mode\":{\"extension\":[{\"url\":\"http://hl7.org/fhir/StructureDefinition/data-absent-reason\",\"valueCode\":\"unknown\"}]},\"_score\":{\"extension\":[{\"url\":\"http://hl7.org/fhir/StructureDefinition/data-absent-reason\",\"valueCode\":\"unknown\"}]}}",
            };

            foreach (string search in searches)
            {
                foreach (bool fullUrl in new[] { false, true })
                {
                    foreach (bool requestAndResponse in new[] { false, true })
                    {
                        foreach (bool pretty in new[] { false, true })
                        {
                            yield return new object[] { search, fullUrl, requestAndResponse, pretty };
                        }
                    }
                }
            }
        }

        [Theory]
        [MemberData(nameof(SearchMetadataCases))]
        public async Task GivenSearchMetadata_WhenSerialized_ThenMatchesBuiltInSerializer(
            string searchJson,
            bool fullUrl,
            bool requestAndResponse,
            bool pretty)
        {
            var observation = Samples.GetDefaultObservation().ToPoco();
            observation.Id = "observation";
            var (rawBundle, bundle) = CreateBundle(Samples.GetDefaultPatient(), observation.ToResourceElement());
            var parser = new FhirJsonParser(DefaultParserSettings.Settings);
            SearchComponent search = searchJson == null ? null : parser.Parse<Hl7.Fhir.Model.Bundle>(
                "{\"resourceType\":\"Bundle\",\"entry\":[{\"search\":" + searchJson + "}]}").Entry.Single().Search;
            for (int i = 0; i < rawBundle.Entry.Count; i++)
            {
                rawBundle.Entry[i].Search = bundle.Entry[i].Search = search;
                rawBundle.Entry[i].FullUrl = bundle.Entry[i].FullUrl = fullUrl ? $"https://example.org/{bundle.Entry[i].Resource.TypeName}/{bundle.Entry[i].Resource.Id}" : null;
                if (!requestAndResponse)
                {
                    rawBundle.Entry[i].Request = bundle.Entry[i].Request = null;
                    rawBundle.Entry[i].Response = bundle.Entry[i].Response = null;
                }
            }

            using var stream = new MemoryStream();
            await _bundleSerializer.Serialize(rawBundle, stream, pretty);

            string actual = Encoding.UTF8.GetString(stream.ToArray());
            Assert.True(JToken.DeepEquals(JToken.Parse(bundle.ToJson()), JToken.Parse(actual)), actual);
            Assert.All(rawBundle.Entry, entry => Assert.Null(entry.Resource));
        }

        [Fact]
        public async Task GivenSearchScoreAndNonEnglishCulture_WhenSerialized_ThenDecimalUsesInvariantFormat()
        {
            var (rawBundle, bundle) = CreateBundle(Samples.GetDefaultPatient());
            rawBundle.Entry.Single().Search = bundle.Entry.Single().Search = new SearchComponent { Score = 0.91m };
            CultureInfo originalCulture = CultureInfo.CurrentCulture;

            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                using var stream = new MemoryStream();

                await _bundleSerializer.Serialize(rawBundle, stream);

                string actual = Encoding.UTF8.GetString(stream.ToArray());
                using var document = JsonDocument.Parse(actual);
                var score = document.RootElement.GetProperty("entry")[0].GetProperty("search").GetProperty("score");
                Assert.Equal(JsonValueKind.Number, score.ValueKind);
                Assert.Equal("0.91", score.GetRawText());
                Assert.True(JToken.DeepEquals(JToken.Parse(bundle.ToJson()), JToken.Parse(actual)), actual);
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GivenRawResourceAndSearchMetadata_WhenSerialized_ThenResourceBytesAreUnchanged(bool pretty)
        {
            const string resourceJson = """
                { "resourceType":"Patient", "id":"raw-patient", "meta":{"versionId":"1","lastUpdated":"2020-01-01T00:00:00Z"},
                  "name":[{"text":"Jos\u00e9 / 名"}], "extension":[{"url":"http://example.org/decimal","valueDecimal":1.2300}] }
                """;
            var wrapper = new ResourceWrapper(
                "raw-patient",
                "1",
                "Patient",
                new RawResource(resourceJson, FhirResourceFormat.Json, isMetaSet: true),
                null,
                DateTimeOffset.Parse("2020-01-01T00:00:00Z", CultureInfo.InvariantCulture),
                false,
                null,
                null,
                null);
            var entry = new RawBundleEntryComponent(wrapper)
            {
                Search = new SearchComponent { Mode = SearchEntryMode.Match, Score = 0.91m },
            };
            var bundle = new Hl7.Fhir.Model.Bundle { Type = BundleType.Searchset, Entry = { entry } };
            using var stream = new MemoryStream();

            await _bundleSerializer.Serialize(bundle, stream, pretty);

            using var document = JsonDocument.Parse(stream.ToArray());
            var serializedEntry = document.RootElement.GetProperty("entry")[0];
            Assert.Equal(
                Encoding.UTF8.GetBytes(resourceJson),
                Encoding.UTF8.GetBytes(serializedEntry.GetProperty("resource").GetRawText()));
            Assert.Equal(0.91m, serializedEntry.GetProperty("search").GetProperty("score").GetDecimal());
            Assert.Null(entry.Resource);
            Assert.Equal(resourceJson, wrapper.RawResource.Data);
        }

        private async Task Validate(Hl7.Fhir.Model.Bundle rawBundle, Hl7.Fhir.Model.Bundle bundle)
        {
            string serialized;

            using (var ms = new MemoryStream())
               using (var sr = new StreamReader(ms))
            {
                await _bundleSerializer.Serialize(rawBundle, ms);

                ms.Seek(0, SeekOrigin.Begin);
                serialized = await sr.ReadToEndAsync();
            }

            string originalSerializer = bundle.ToJson();
            Assert.Equal(originalSerializer, serialized);

            var deserializedBundle = new FhirJsonParser(DefaultParserSettings.Settings).Parse(serialized) as Hl7.Fhir.Model.Bundle;

            Assert.True(deserializedBundle.IsExactly(bundle));
        }

        private (Hl7.Fhir.Model.Bundle rawBundle, Hl7.Fhir.Model.Bundle bundle) CreateBundle(params ResourceElement[] resources)
        {
            string id = Guid.NewGuid().ToString();
            var rawBundle = new Hl7.Fhir.Model.Bundle();
            var bundle = new Hl7.Fhir.Model.Bundle();

            rawBundle.Id = bundle.Id = id;
            rawBundle.Type = bundle.Type = BundleType.Searchset;
            rawBundle.Entry = new List<EntryComponent>();
            rawBundle.Total = resources.Count();
            bundle.Entry = new List<EntryComponent>();
            bundle.Total = resources.Count();

            foreach (var resource in resources)
            {
                var poco = resource.ToPoco();
                poco.VersionId = "1";
                poco.Meta.LastUpdated = Clock.UtcNow;
                poco.Meta.Tag = new List<Hl7.Fhir.Model.Coding>
                {
                    new Hl7.Fhir.Model.Coding { System = "testTag", Code = Guid.NewGuid().ToString() },
                };
                var wrapper = _wrapperFactory.Create(poco.ToResourceElement(), deleted: false, keepMeta: true);
                wrapper.Version = "1";

                var requestComponent = new RequestComponent { Method = HTTPVerb.POST, Url = "patient/" };
                var responseComponent = new ResponseComponent { Etag = "W/\"1\"", LastModified = DateTimeOffset.UtcNow, Status = "201 Created" };
                rawBundle.Entry.Add(new RawBundleEntryComponent(wrapper)
                {
                    Request = requestComponent,
                    Response = responseComponent,
                });
                bundle.Entry.Add(new EntryComponent { Resource = poco, Request = requestComponent, Response = responseComponent });
            }

            return (rawBundle, bundle);
        }
    }
}
