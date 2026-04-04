// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.SqlOnFhir.Channels;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.SqlOnFhir.Tests.Channels;

/// <summary>
/// Unit tests for <see cref="ViewDefinitionSubscriptionManager"/>.
/// Tests the Subscription resource building logic without requiring MediatR or a data store.
/// </summary>
public class ViewDefinitionSubscriptionManagerTests
{
    private const string PatientViewDefinitionJson = """
        {
            "name": "patient_demographics",
            "resource": "Patient",
            "select": [{ "column": [{ "name": "id", "path": "id" }] }]
        }
        """;

    private const string BpViewDefinitionJson = """
        {
            "name": "us_core_blood_pressures",
            "resource": "Observation",
            "select": [{ "column": [{ "name": "id", "path": "id" }] }],
            "where": [{"path": "code.coding.exists(system='http://loinc.org' and code='85354-9')"}]
        }
        """;

    [Fact]
    public void GivenPatientViewDef_WhenBuildingSubscription_ThenResourceTypeFilterIsPatient()
    {
        Subscription sub = ViewDefinitionSubscriptionManager.BuildSubscriptionResource(
            PatientViewDefinitionJson,
            "patient_demographics",
            "Patient");

        // Verify criteria extension contains Patient resource type filter
        Assert.NotNull(sub.CriteriaElement);
        var filterExt = sub.CriteriaElement.Extension.FirstOrDefault(
            e => e.Url.Contains("backport-filter-criteria"));
        Assert.NotNull(filterExt);
        Assert.Equal("Patient?", ((FhirString)filterExt!.Value).Value);
    }

    [Fact]
    public void GivenObservationViewDef_WhenBuildingSubscription_ThenResourceTypeFilterIsObservation()
    {
        Subscription sub = ViewDefinitionSubscriptionManager.BuildSubscriptionResource(
            BpViewDefinitionJson,
            "us_core_blood_pressures",
            "Observation");

        var filterExt = sub.CriteriaElement.Extension.FirstOrDefault(
            e => e.Url.Contains("backport-filter-criteria"));
        Assert.NotNull(filterExt);
        Assert.Equal("Observation?", ((FhirString)filterExt!.Value).Value);
    }

    [Fact]
    public void GivenViewDef_WhenBuildingSubscription_ThenChannelTypeIsViewDefinitionRefresh()
    {
        Subscription sub = ViewDefinitionSubscriptionManager.BuildSubscriptionResource(
            PatientViewDefinitionJson,
            "patient_demographics",
            "Patient");

        var channelTypeExt = sub.Channel.TypeElement.Extension.FirstOrDefault(
            e => e.Url.Contains("backport-channel-type"));
        Assert.NotNull(channelTypeExt);
        Assert.Equal("view-definition-refresh", ((Coding)channelTypeExt!.Value).Code);
    }

    [Fact]
    public void GivenViewDef_WhenBuildingSubscription_ThenHeadersContainViewDefinitionMetadata()
    {
        Subscription sub = ViewDefinitionSubscriptionManager.BuildSubscriptionResource(
            PatientViewDefinitionJson,
            "patient_demographics",
            "Patient");

        Assert.NotNull(sub.Channel.Header);
        Assert.Equal(2, sub.Channel.Header.Count());

        Assert.Contains(sub.Channel.Header, h => h.StartsWith("viewDefinitionName: "));
        Assert.Contains(sub.Channel.Header, h => h.StartsWith("viewDefinitionJson: "));

        string nameHeader = sub.Channel.Header.First(h => h.StartsWith("viewDefinitionName: "));
        Assert.Equal("viewDefinitionName: patient_demographics", nameHeader);
    }

    [Fact]
    public void GivenViewDef_WhenBuildingSubscription_ThenBackportProfileIsSet()
    {
        Subscription sub = ViewDefinitionSubscriptionManager.BuildSubscriptionResource(
            PatientViewDefinitionJson,
            "patient_demographics",
            "Patient");

        Assert.NotNull(sub.Meta?.Profile);
        Assert.Contains(
            "http://hl7.org/fhir/uv/subscriptions-backport/StructureDefinition/backport-subscription",
            sub.Meta.Profile);
    }

    [Fact]
    public void GivenViewDef_WhenBuildingSubscription_ThenStatusIsRequested()
    {
        Subscription sub = ViewDefinitionSubscriptionManager.BuildSubscriptionResource(
            PatientViewDefinitionJson,
            "patient_demographics",
            "Patient");

        Assert.Equal(Subscription.SubscriptionStatus.Requested, sub.Status);
    }

    [Fact]
    public void GivenViewDef_WhenBuildingSubscription_ThenEndpointIsInternalUri()
    {
        Subscription sub = ViewDefinitionSubscriptionManager.BuildSubscriptionResource(
            PatientViewDefinitionJson,
            "patient_demographics",
            "Patient");

        Assert.Equal("internal://sqlfhir/patient_demographics", sub.Channel.Endpoint);
    }

    [Fact]
    public void GivenViewDef_WhenBuildingSubscription_ThenPayloadIsFullResource()
    {
        Subscription sub = ViewDefinitionSubscriptionManager.BuildSubscriptionResource(
            PatientViewDefinitionJson,
            "patient_demographics",
            "Patient");

        var payloadExt = sub.Channel.PayloadElement.Extension.FirstOrDefault(
            e => e.Url.Contains("backport-payload-content"));
        Assert.NotNull(payloadExt);
        Assert.Equal("full-resource", ((Code)payloadExt!.Value).Value);
    }

    [Fact]
    public void GivenViewDef_WhenBuildingSubscription_ThenMaxCountExtensionPresent()
    {
        Subscription sub = ViewDefinitionSubscriptionManager.BuildSubscriptionResource(
            PatientViewDefinitionJson,
            "patient_demographics",
            "Patient");

        var maxCountExt = sub.Channel.Extension.FirstOrDefault(
            e => e.Url.Contains("backport-max-count"));
        Assert.NotNull(maxCountExt);
        Assert.Equal(100, ((PositiveInt)maxCountExt!.Value).Value);
    }
}
