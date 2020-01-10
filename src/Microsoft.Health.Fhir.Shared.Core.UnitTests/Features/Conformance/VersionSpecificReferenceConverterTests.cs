// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Conformance.Serialization;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Conformance
{
    public class VersionSpecificReferenceConverterTests
    {
        private readonly IModelInfoProvider _modelInfoProvider;

        public VersionSpecificReferenceConverterTests()
        {
            _modelInfoProvider = Substitute.For<IModelInfoProvider>();
        }

        [Fact]
        public void GivenAReferenceObject_WhenConvertingToJsonInStu3_ThenOneOptionIsSerializedAsPerStu3InsteadOfAList()
        {
            _modelInfoProvider.Version.Returns(FhirSpecification.R4);
            var json = GetJson("B");

            Assert.Equal("{\"prop1\":\"B\"}", json);
        }

        [Fact]
        public void GivenAReferenceObject_WhenConvertingToJsonInR4_ThenOneOptionIsSerializedAsPerR4InsteadOfAList()
        {
            var json = GetJson("B");

            Assert.Equal("{\"prop1\":{\"reference\":\"B\"}}", json);
        }

        private string GetJson(string referenceObject)
        {
            var obj = new
            {
                Prop1 = new ReferenceObjectHashSet<string>(referenceObject)
                {
                    "A",
                    "B",
                    "C",
                },
            };

            return JsonConvert.SerializeObject(obj, new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Converters = new List<JsonConverter>
                {
                    new VersionSpecificReferenceConverter(_modelInfoProvider),
                },
                NullValueHandling = NullValueHandling.Ignore,
            });
        }
    }
}
