// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Specification;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SourceNodeSerialization.Extensions;
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

    private readonly string _patientJson = @"{
  ""resourceType"" : ""Patient"",
  ""id"" : ""example"",
  ""name"" : [{
    ""id"" : ""f2"",
    ""use"" : ""official"" ,
    ""given"" : [ ""Karen"", ""May"" ],
    ""_given"" : [ null, {""id"" : ""middle""} ],
    ""family"" :  ""Van"",
    ""_family"" : {""id"" : ""a2""}
   }],
  ""meta"" : {
    ""lastUpdated"" : ""2023-10-01T12:00:00Z"",
    ""versionId"" : ""-1"",
    ""extension"" : [
      {
        ""url"" : ""http://example.com/deleted-state"",
        ""valueCode"" : ""soft-deleted""
      }
    ]
  },
  ""text"" : {
    ""status"" : ""generated"" ,
    ""div"" : ""<div xmlns=\""http://www.w3.org/1999/xhtml\""><p>...</p></div>""
  }
}";

    private readonly string _patientMinExtJson = @"{
  ""resourceType"" : ""Patient"",
  ""name"" : [{
    ""use"" : ""official"" ,
    ""given"" : [ ""Karen"", ""May"" ],
    ""family"" :  ""Van""
   }],
  ""meta"" : {
    ""extension"" : [
      {
        ""url"" : ""http://example.com/deleted-state"",
        ""valueCode"" : ""soft-deleted""
      }
    ]
  }
}";

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

    [Fact]
    public void ReadShadowProperty()
    {
        ISourceNode sourceNode = JsonSourceNodeFactory.Parse(_patientJson);
        ITypedElement node = sourceNode.ToTypedElement(ModelInfoProvider.StructureDefinitionSummaryProvider);

        object familyName = node.Scalar("Patient.name.family");
        object familyId = node.Scalar("Patient.name.family.id");
        Assert.Equal("Van", familyName);
        Assert.Equal("a2", familyId);

        object middle = node.Scalar("Patient.name.given[1]");
        object middleId = node.Scalar("Patient.name.given[1].id");
        Assert.Equal("May", middle);
        Assert.Equal("middle", middleId);

        object firstName = node.Scalar("Patient.name.given[0]");
        object firstNameId = node.Scalar("Patient.name.given[0].id");
        Assert.Equal("Karen", firstName);
        Assert.Null(firstNameId);
    }

    [Fact]
    public void ReadExtension()
    {
        ISourceNode sourceNode = JsonSourceNodeFactory.Parse(_patientMinExtJson);
        ITypedElement node = sourceNode.ToTypedElement(ModelInfoProvider.StructureDefinitionSummaryProvider);

        var compare = FhirJsonNode.Parse(_patientMinExtJson).ToTypedElement(ModelInfoProvider.StructureDefinitionSummaryProvider);

        var path = "Resource.meta.extension.where(url = 'http://example.com/deleted-state').where(value = 'soft-deleted')";

        var value1 = node.Select(path).ToArray();
        var value2 = compare.Select(path).ToArray();

        Assert.Equal(value2.Length, value1.Length);

        var scalar = node.Scalar(path + ".exists()");
        Assert.Equal(true, scalar);
    }

    [Fact]
    public void RemoveExtension()
    {
        var extensionUrl = "http://example.com/deleted-state";
        var model = ResourceJsonNode.Parse(_patientMinExtJson);
        model.Meta.RemoveExtension(extensionUrl);

        var json = model.SerializeToString();
        Assert.False(json.Contains(extensionUrl, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SourceNode()
    {
        ISourceNode sourceNode = JsonSourceNodeFactory.Parse(_patientJson);

        ITypedElement node = sourceNode.ToTypedElement(ModelInfoProvider.StructureDefinitionSummaryProvider);
        ITypedElement familyType = node.Select("Patient.name.family").Single();

        IReadOnlyCollection<IElementDefinitionSummary> definitions = familyType.ChildDefinitions(ModelInfoProvider.StructureDefinitionSummaryProvider);
    }

    [Fact]
    public void FindId()
    {
        ISourceNode sourceNode = JsonSourceNodeFactory.Parse(_patientJson);

        ITypedElement node = sourceNode.ToTypedElement(ModelInfoProvider.StructureDefinitionSummaryProvider);
        ITypedElement id = node.Select("Resource.id").Single();
        Assert.Equal("example",  id.Value);
    }

    [Fact]
    public void CanFindReferenceValuesInSourceNode()
    {
        ISourceNode sourceNode = JsonSourceNodeFactory.Parse(Samples.GetDefaultObservation().Poco.Value.ToJson());

        IEnumerable<(string Path, string ReferenceValue)> references = sourceNode
            .ToTypedElement(ModelInfoProvider.StructureDefinitionSummaryProvider)
            .GetReferenceValues();

        var reference = Assert.Single(references);

        Assert.Contains("Observation.subject[0]", reference.Path);
        Assert.Contains("Patient/example", reference.ReferenceValue);
    }

    [Fact]
    public void ExtractEffectiveDateTime()
    {
        var poco = (Observation)Samples.GetDefaultObservation().Poco.Value;
        poco.Effective = new FhirDateTime(_currentDate.Year);

        ISourceNode sourceNode = JsonSourceNodeFactory.Parse(poco.ToJson());

        var effectiveDatePath = "List.date | Observation.effective | Procedure.performed | (RiskAssessment.occurrence as dateTime)";

        var effectiveExpected = poco.ToTypedElement().Select(effectiveDatePath).Single();

        ITypedElement node = sourceNode.ToTypedElement(ModelInfoProvider.StructureDefinitionSummaryProvider);
        ITypedElement effectiveActual = node.Select(effectiveDatePath).Single();

        Assert.Equal(effectiveExpected.Value, effectiveActual.Value);
    }
}
