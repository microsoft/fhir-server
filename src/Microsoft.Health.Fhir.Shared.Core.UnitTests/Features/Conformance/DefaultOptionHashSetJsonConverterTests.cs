// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Conformance.Serialization;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Conformance
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class DefaultOptionHashSetJsonConverterTests
    {
        [Fact]
        public void GivenAOptionHashSet_WhenConvertingToJson_ThenOneOptionIsSerializedInsteadOfAList()
        {
            var json = GetJson("B");

            Assert.Equal("{\"prop1\":\"B\"}", json);
        }

        [Fact]
        public void GivenAOptionHashSet_WhenConvertingToJsonWithInvalidOption_ThenFirstOptionIsSerializedInsteadOfAList()
        {
            Assert.Throws<UnsupportedConfigurationException>(() => GetJson("D"));
        }

        private string GetJson(string defaultOption)
        {
            var obj = new
            {
                Prop1 = new DefaultOptionHashSet<string>(defaultOption)
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
                    new DefaultOptionHashSetJsonConverter(),
                },
                NullValueHandling = NullValueHandling.Ignore,
            });
        }
    }
}
