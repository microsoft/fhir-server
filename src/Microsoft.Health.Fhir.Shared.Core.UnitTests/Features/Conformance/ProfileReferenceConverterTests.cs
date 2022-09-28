// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Conformance.Serialization;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Conformance
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class ProfileReferenceConverterTests
    {
        private readonly IModelInfoProvider _modelInfoProvider;

        public ProfileReferenceConverterTests()
        {
            _modelInfoProvider = Substitute.For<IModelInfoProvider>();
        }

        [Fact]
        public void GivenAReferenceObject_WhenConvertingToJsonInStu3_ThenOneOptionIsSerializedAsPerStu3()
        {
            _modelInfoProvider.Version.Returns(FhirSpecification.Stu3);
            var json = GetJson();

            Assert.Equal("{\"reference\":\"http://hl7.org/fhir/StructureDefinition/Account\"}", json);
        }

        [Fact]
        public void GivenAReferenceObject_WhenConvertingToJsonInR4_ThenOneOptionIsSerializedAsPerR4()
        {
            _modelInfoProvider.Version.Returns(FhirSpecification.R4);
            var json = GetJson();

            Assert.Equal("\"http://hl7.org/fhir/StructureDefinition/Account\"", json);
        }

        private string GetJson()
        {
            var obj = new ReferenceComponent()
            {
                    Reference = "http://hl7.org/fhir/StructureDefinition/Account",
            };

            return JsonConvert.SerializeObject(obj, new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Converters = new List<JsonConverter>
                {
                    new ReferenceComponentConverter(_modelInfoProvider),
                },
                NullValueHandling = NullValueHandling.Ignore,
            });
        }
    }
}
