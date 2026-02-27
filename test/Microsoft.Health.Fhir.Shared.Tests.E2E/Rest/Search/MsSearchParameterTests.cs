// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Model;
using Hl7.Fhir.Specification;
using Hl7.Fhir.Utility;
using Microsoft.Health.Extensions.Xunit;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class MsSearchParameterTests : SearchTestsBase<HttpIntegrationTestFixture>
    {
        public MsSearchParameterTests(HttpIntegrationTestFixture fixture)
            : base(fixture)
        {
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenResource_WhenSearchedWithMsSearchParameter_ThenResourcesAreReturned()
        {
            // Create various resources.
            string tag = Guid.NewGuid().ToString();
            var patient = new Patient();

            patient.Meta = new Meta();
            patient.Meta.Tag.Add(new Coding(null, tag));
            patient.Meta.Extension.Add(new Extension("https://azurehealthcareapis.com/data-extensions/expiry-date", new FhirDateTime("2000-01-01")));

            patient.Name = new List<HumanName>()
                {
                    new HumanName() { Family = "test" },
                };

            patient = (await Client.CreateAsync(patient)).Resource;

            await ExecuteAndValidateBundle($"Patient?_expiryDate=lt2025&_tag={tag}", patient);
        }
    }
}
