// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core;
using Microsoft.Health.Fhir.Api.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Compartment;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Shared.Core.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using NSubstitute;
using Xunit;
using static Hl7.Fhir.Model.Bundle;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Api.UnitTests.Features.Resources.Bundle
{
    public class BundleSerializerTests
    {
        private readonly ResourceWrapperFactory _wrapperFactory;

        public BundleSerializerTests()
        {
            var requestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
            requestContextAccessor.FhirRequestContext.Returns(x => new FhirRequestContext("get", "https://localhost/Patient", "https://localhost", "correlation", new Dictionary<string, StringValues>(), new Dictionary<string, StringValues>()));

            _wrapperFactory = new ResourceWrapperFactory(
                                     new RawResourceFactory(new FhirJsonSerializer()),
                                     requestContextAccessor,
                                     Substitute.For<ISearchIndexer>(),
                                     Substitute.For<IClaimsExtractor>(),
                                     Substitute.For<ICompartmentIndexer>());
        }

        [Fact]
        public async Task GivenBundle_WhenSerialized_MatchesSerializationByBuiltInSerializer()
        {
            var patientResource = Samples.GetDefaultPatient().ToPoco<Patient>();
            patientResource.VersionId = "1";
            patientResource.Meta.LastUpdated = Clock.UtcNow;

            var patientWrapper = _wrapperFactory.Create(patientResource.ToResourceElement(), deleted: false, keepMeta: true);
            patientWrapper.Version = "1";

            var bundleWithRawEntry = new Hl7.Fhir.Model.Bundle
            {
                Id = Guid.NewGuid().ToString(),
                Type = BundleType.Searchset,
                Entry = new List<EntryComponent>
                {
                    new RawBundleEntryComponent(patientWrapper),
                },
            };

            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Id = bundleWithRawEntry.Id,
                Type = BundleType.Searchset,
                Entry = new List<EntryComponent>
                {
                    new EntryComponent
                    {
                        Resource = patientResource,
                    },
                },
            };

            string serialized;

            using (var ms = new MemoryStream())
            using (var sr = new StreamReader(ms))
            {
                await BundleSerializer.Serialize(bundleWithRawEntry, ms);

                ms.Seek(0, SeekOrigin.Begin);
                serialized = await sr.ReadToEndAsync();
            }

            string originalSerializer = bundle.ToJson();
            Assert.Equal(originalSerializer, serialized);
            patientWrapper.RawResource.Data = serialized;

            var deserializedBundle = new FhirJsonParser(DefaultParserSettings.Settings).Parse(serialized) as Hl7.Fhir.Model.Bundle;

            Assert.True(deserializedBundle.IsExactly(bundle));
        }
    }
}
