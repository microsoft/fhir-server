// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using Microsoft.Health.Fhir.Core.Features.Conformance.Serialization;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Conformance
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class EnumLiteralJsonConverterTests
    {
        [Fact]
        public void GivenAnObject_WhenSerializingWithEnumLiteral_ThenLiteralIsSerialized()
        {
            var obj = new
            {
                Prop1 = SearchParamType.Number,
            };

            var json = GetJson(obj);

            Assert.Equal("{\"prop1\":\"number\"}", json);
        }

        [Fact]
        public void GivenAnObject_WhenSerializingWithUnmappableEnumLiteral_ThenLiteralIsSerializedToString()
        {
            var obj = new
            {
                Prop1 = HttpStatusCode.BadRequest,
            };

            var json = GetJson(obj);

            Assert.Equal("{\"prop1\":\"BadRequest\"}", json);
        }

        private string GetJson(object obj)
        {
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
