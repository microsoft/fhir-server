// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Persistence
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class RawResourceTests
    {
        [Fact]
        public void GivenAResource_WhenCreateARawResourceWithKeepMetaFalse_ThenTheObjectPassedInIsModified()
        {
            var serializer = new FhirJsonSerializer();
            var rawResourceFactory = new RawResourceFactory(serializer);

            string versionId = Guid.NewGuid().ToString();
            var observation = Samples.GetDefaultObservation()
                .UpdateVersion(versionId);

            Assert.NotNull(observation.VersionId);

            var raw = rawResourceFactory.Create(observation, keepMeta: false);

            Assert.NotNull(raw.Data);

            var parser = new FhirJsonParser(DefaultParserSettings.Settings);

            var deserialized = parser.Parse<Observation>(raw.Data);

            Assert.NotNull(deserialized);
            Assert.Null(deserialized.VersionId);
            Assert.Null(deserialized.Meta?.LastUpdated);
        }
    }
}
