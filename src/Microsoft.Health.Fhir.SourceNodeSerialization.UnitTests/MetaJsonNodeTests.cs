// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.SourceNodeSerialization.SourceNodes.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SourceNodeSerialization.UnitTests;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Search)]
public class MetaJsonNodeTests
{
    private readonly Patient _patientPoco;
    private readonly ResourceJsonNode _patientJsonNode;
    private readonly DateTimeOffset _currentDate;
    private readonly string _updatedJson;

    public MetaJsonNodeTests()
    {
        ModelExtensions.SetModelInfoProvider();
        _currentDate = DateTimeOffset.UtcNow;
        _patientPoco = Samples.GetDefaultPatient().ToPoco<Patient>();
        _patientPoco.Meta = new Meta
        {
            LastUpdated = _currentDate,
            VersionId = "-1",
        };
        _updatedJson = _patientPoco.ToJson();

        _patientJsonNode = JsonSourceNodeFactory.ParseJsonNode<ResourceJsonNode>(Samples.GetJson("Patient"));
    }

    [Fact]
    public void GivenAPatientPoco_WhenConvertingToJsonNode_ThenMetaIsPopulated()
    {
        _patientJsonNode.Meta.LastUpdated = _currentDate;
        _patientJsonNode.Meta.VersionId = "-1";

        var newJson = _patientJsonNode.SerializeToString().Replace("\\u002B", "+");

        var deserializer = new FhirJsonPocoDeserializer();
        Resource deserializedPatient = deserializer.DeserializeResource(newJson);

        Assert.Equal(_currentDate, deserializedPatient.Meta.LastUpdated);
        Assert.Equal("-1", deserializedPatient.Meta.VersionId);
    }
}
