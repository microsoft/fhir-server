// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Shared.Core.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
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
        private readonly BundleSerializer _bundleSerializer = new BundleSerializer();

        public BundleSerializerTests()
        {
            var requestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            requestContextAccessor.RequestContext.Returns(x => new FhirRequestContext("get", "https://localhost/Patient", "https://localhost", "correlation", new Dictionary<string, StringValues>(), new Dictionary<string, StringValues>()));

            _wrapperFactory = new ResourceWrapperFactory(
                                     new RawResourceFactory(new IgnixaJsonSerializer(), new FhirJsonSerializer()),
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

            // Note: We don't compare exact strings because Ignixa and Firely may encode
            // certain characters differently (e.g., '+' vs '\u002B'). Both are valid JSON.
            // The semantic comparison via IsExactly() below verifies correctness.
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
