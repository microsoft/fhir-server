// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Persistence;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Serialization)]
public class SdkModeRawResourceFactoryTests
{
    [Fact]
    public void GivenFirelyFactory_WhenCreatingRawResource_ThenFirelyJsonIsProduced()
    {
        var patient = new Patient { Id = "firely-patient", Active = true };
        var factory = new FirelyRawResourceFactory(new FhirJsonSerializer());

        var raw = factory.Create(patient.ToResourceElement(), keepMeta: true);

        Assert.Equal(FhirResourceFormat.Json, raw.Format);
        Assert.Contains("\"resourceType\":\"Patient\"", raw.Data);
        Assert.Contains("\"id\":\"firely-patient\"", raw.Data);
    }

    [Fact]
    public void GivenIgnixaFactoryAndIgnixaResource_WhenCreatingRawResource_ThenIgnixaJsonIsProduced()
    {
        var serializer = new IgnixaJsonSerializer();
        var schemaContext = new IgnixaSchemaContext(ModelInfoProvider.Instance);
        var node = serializer.Parse("{\"resourceType\":\"Patient\",\"id\":\"ignixa-patient\",\"active\":true}");
        var element = new IgnixaResourceElement(node, schemaContext.Schema).ToResourceElement();
        var factory = new IgnixaModeRawResourceFactory(serializer);

        var raw = factory.Create(element, keepMeta: true);

        Assert.Equal(FhirResourceFormat.Json, raw.Format);
        Assert.Contains("\"resourceType\":\"Patient\"", raw.Data);
        Assert.Contains("\"id\":\"ignixa-patient\"", raw.Data);
    }

    [Fact]
    public void GivenIgnixaFactoryAndFirelyResource_WhenCreatingRawResource_ThenInvalidOperationExceptionIsThrown()
    {
        var patient = new Patient { Id = "firely-patient" };
        var factory = new IgnixaModeRawResourceFactory(new IgnixaJsonSerializer());

        Assert.Throws<InvalidOperationException>(() => factory.Create(patient.ToResourceElement(), keepMeta: true));
    }
}
