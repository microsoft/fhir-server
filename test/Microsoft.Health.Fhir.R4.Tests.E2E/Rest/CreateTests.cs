// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Net;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.Fakes;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest;

/// <summary>
/// R4 Create Tests
/// </summary>
public partial class CreateTests
{
    [SkippableTheory]
    [MemberData(nameof(FhirTypesAsStrings))]
    [Trait(Traits.Priority, Priority.One)]
    public async System.Threading.Tasks.Task GivenARandomlyGeneratedResource_WhenPostingToHttp_ThenTheServerShouldRespondSuccessfully(string resourceType)
    {
        Skip.If(resourceType == nameof(SearchParameter), "SearchParameter is not supported in this test.");
        Skip.If(resourceType == nameof(Parameters), "Parameters is not valid in this test.");
        Skip.If(resourceType == nameof(CapabilityStatement), "CapabilityStatement is not valid in this test.");
        Skip.If(resourceType == nameof(OperationOutcome), "OperationOutcome is not valid in this test.");

        var type = ModelInfo.GetTypeForFhirType(resourceType);
        var resource = FhirFakesFactory.Create(type);

        var resourceJson = resource.ToJson(new FhirJsonSerializationSettings { Pretty = true });
        _outputHelper.WriteLine(resourceJson);

        using FhirResponse<Resource> response = await _client.PostAsync(resourceType, resourceJson);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    [Trait(Traits.Priority, Priority.One)]
    public async System.Threading.Tasks.Task GivenAnGeneratedBundleOfPatientCompartmentResources_WhenPosting_ThenTheServerShouldRespondSuccessfully()
    {
        var bundle = FhirFakesFactory.CreatePatientCompartmentBundle(
            observations: 10,
            conditions: 10,
            encounter: 10,
            meds: 10,
            immunizations: 10,
            procedures: 10,
            reports: 10);

        using FhirResponse<Bundle> response = await _client.PostBundleAsync(bundle);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Resource.Entry.All(x => x.Response.Status.StartsWith("201")));
    }

    public static IEnumerable<object[]> FhirTypesAsStrings()
    {
        return ModelInfo.SupportedResources.Select(x => new[] { x });
    }
}
