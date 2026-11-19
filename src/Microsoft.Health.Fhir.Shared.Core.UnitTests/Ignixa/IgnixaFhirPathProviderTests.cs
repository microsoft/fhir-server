// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.FhirPath;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Ignixa;

/// <summary>
/// Verifies that <see cref="IgnixaFhirPathProvider"/> evaluates expressions against resources parsed by
/// <see cref="IgnixaJsonSerializer"/> and exposed through <see cref="IgnixaResourceElement"/>. Provider
/// behaviour on Firely POCO input is covered by <c>FhirPathProviderTests</c>.
/// </summary>
[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Search)]
public class IgnixaFhirPathProviderTests
{
    private readonly IgnixaFhirPathProvider _provider;
    private readonly IgnixaSchemaContext _schemaContext;
    private readonly IgnixaJsonSerializer _serializer = new();

    public IgnixaFhirPathProviderTests()
    {
        ModelExtensions.SetModelInfoProvider();

        _schemaContext = new IgnixaSchemaContext(ModelInfoProvider.Instance);
        _provider = new IgnixaFhirPathProvider(_schemaContext);
    }

    [Theory]
    [InlineData("Patient.id")]
    [InlineData("Patient.name")]
    [InlineData("Patient.name.family")]
    [InlineData("Patient.identifier")]
    public void GivenAnIgnixaParsedPatient_WhenExpressionIsEvaluated_ThenValuesAreReturned(string expression)
    {
        ITypedElement patient = ParseSamplePatient();

        ITypedElement[] results = _provider.Compile(expression).Select(patient).ToArray();

        Assert.NotEmpty(results);
    }

    [Fact]
    public void GivenAnIgnixaParsedPatient_WhenScalarExpressionIsEvaluated_ThenStringValueIsReturned()
    {
        ITypedElement patient = ParseSamplePatient();

        object id = _provider.Compile("Patient.id").Select(patient).Single().Value;

        Assert.IsType<string>(id);
        Assert.NotEmpty((string)id);
    }

    private ITypedElement ParseSamplePatient()
    {
        var node = _serializer.Parse(Samples.GetJson("Patient"));
        return new IgnixaResourceElement(node, _schemaContext.Schema).ToTypedElement();
    }
}
