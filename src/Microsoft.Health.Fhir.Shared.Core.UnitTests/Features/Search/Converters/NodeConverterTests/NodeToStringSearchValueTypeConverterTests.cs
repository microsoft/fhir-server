// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Converters.NodeConverterTests
{
    public class NodeToStringSearchValueTypeConverterTests
    {
        [Fact]
        public void TestConverter()
        {
            var person = Samples.GetDefaultPatient().ToPoco<Patient>();
            person.Address.Add(
                new Address
            {
                City = "Test1",
                Use = Address.AddressUse.Old,
                State = "WA",
            });

            var p = person.ToJson();

            var json = FhirJsonNode.Parse(p);
            var el = json.ToTypedElement(ModelInfoProvider.StructureDefinitionSummaryProvider);

            var address = el.Select("Patient.address").ToArray();

            var converter = new AddressNodeToStringSearchValueConverter();
            var stringConverter = new StringNodeToStringSearchValueConverter();
            var values = converter.ConvertTo(address[0]).ToArray();

            var city = el.Select("Patient.address.city | Person.address.city | Practitioner.address.city | RelatedPerson.address.city").ToArray();

            var cityValues = stringConverter.ConvertTo(city[0]).ToArray();
        }
    }
}
