// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Conformance.Serialization;
using Microsoft.Health.Fhir.ValueSets;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Conformance
{
    public class EnumLiteralJsonConverterTests
    {
        [Fact]
        public void GivenAnObject_WhenSerializingWithEnumLiteral_ThenLiteralIsSerialized()
        {
            var json = GetJson();

            Assert.Equal("{\"prop1\":\"number\"}", json);
        }

        private string GetJson()
        {
            var obj = new
            {
                Prop1 = SearchParamType.Number,
            };

            return JsonConvert.SerializeObject(obj, new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Converters = new List<JsonConverter>
                {
                    new EnumLiteralJsonConverter(),
                },
                NullValueHandling = NullValueHandling.Ignore,
            });
        }
    }
}
