// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Tests.Common;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Persistence
{
    public class RawResourceTests
    {
        [Fact]
        public void GivenAResource_WhenCreatingARawResource_ThenTheObjectPassInIsNotModified()
        {
            var serializer = new FhirJsonSerializer();
            var rawResourceFactory = new RawResourceFactory(serializer);

            string versionId = Guid.NewGuid().ToString();
            var observation = Samples.GetDefaultObservation()
                .UpdateVersion(versionId);

            Assert.NotNull(observation.VersionId);

            var raw = rawResourceFactory.Create(observation);

            Assert.NotNull(raw.Data);
            Assert.Equal(versionId, observation.VersionId);
        }
    }
}
