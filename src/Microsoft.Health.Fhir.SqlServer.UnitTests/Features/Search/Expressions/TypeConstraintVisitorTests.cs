// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using static Microsoft.Health.Fhir.Core.Features.Search.Expressions.Expression;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class TypeConstraintVisitorTests
    {
        private const byte AllergyIntolerance = 1;
        private const byte Claim = 2;
        private const byte Condition = 3;
        private const byte Device = 4;
        private const byte DiagnosticReport = 5;
        private const byte Encounter = 6;
        private const byte Immunization = 7;
        private const byte Observation = 8;
        private const byte Patient = 9;
        private const byte Procedure = 10;

        private static readonly byte[] AllTypes = Enumerable.Range(AllergyIntolerance, Procedure - AllergyIntolerance + 1).Select(i => (byte)i).ToArray();
        private static readonly ISqlServerFhirModel FhirModel = CreateFhirModel();
        private static readonly SearchParameterInfo TypeParameter = new(SearchParameterNames.ResourceType, SearchParameterNames.ResourceType);
        private static readonly SearchParameterInfo IdParameter = new(SearchParameterNames.Id, SearchParameterNames.Id);

        public static readonly TheoryData<Expression, byte[]> Data = new()
        {
            { null, AllTypes },
            { SearchParameter(IdParameter, Token("foo")), AllTypes },
            { SearchParameter(TypeParameter, Token(nameof(Patient))), new[] { Patient } },
            { And(SearchParameter(TypeParameter, Token(nameof(Patient))), SearchParameter(TypeParameter, Token(nameof(Observation)))), null },
            { SearchParameter(TypeParameter, Or(Token(nameof(Patient)), Token(nameof(Encounter)))), new[] { Patient, Encounter } },
            { And(SearchParameter(TypeParameter, Token(nameof(Patient))), SearchParameter(TypeParameter, Or(Token(nameof(Patient)), Token(nameof(Encounter))))), new[] { Patient } },
            { And(SearchParameter(TypeParameter, Token(nameof(Patient))), SearchParameter(TypeParameter, Or(Token(nameof(Device)), Token(nameof(Encounter))))), null },
            { And(SearchParameter(TypeParameter, Or(Token(nameof(Patient)), Token(nameof(Encounter)))), SearchParameter(TypeParameter, Token(nameof(Patient)))), new[] { Patient } },
            { new SqlRootExpression(Array.Empty<SearchParamTableExpression>(), new[] { SearchParameter(TypeParameter, Token(nameof(Patient))) }), new[] { Patient } },
            { new SqlRootExpression(Array.Empty<SearchParamTableExpression>(), new[] { SearchParameter(TypeParameter, Or(Token(nameof(Patient)), Token(nameof(Encounter)))) }), new[] { Patient, Encounter } },
        };

        [Theory]
        [MemberData(nameof(Data))]
        public void GivenAnExpression_WhenVisited_DeterminesTheCorrectAllowedTypes(Expression expression, byte[] expectedTypeIds)
        {
            var visitor = new TypeConstraintVisitor();

            var result = visitor.Visit(expression, FhirModel);

            AssertAllowed(result, expectedTypeIds);
        }

        private static void AssertAllowed((byte? singleAllowedResourceTypeId, BitArray allAllowedTypes) result, params byte[] expectedIds)
        {
            expectedIds ??= Array.Empty<byte>();

            switch (expectedIds.Length)
            {
                case 0:
                    Assert.Null(result.singleAllowedResourceTypeId);
                    Assert.Null(result.allAllowedTypes);
                    return;
                case 1:
                    Assert.Equal(expectedIds[0], result.singleAllowedResourceTypeId);
                    break;
                default:
                    Assert.Null(result.singleAllowedResourceTypeId);
                    break;
            }

            for (var i = FhirModel.ResourceTypeIdRange.lowestId; i <= FhirModel.ResourceTypeIdRange.highestId; i++)
            {
                Assert.Equal(expectedIds.Contains(i), result.allAllowedTypes[i]);
            }
        }

        private static ISqlServerFhirModel CreateFhirModel()
        {
            var sqlServerFhirModel = Substitute.For<ISqlServerFhirModel>();
            sqlServerFhirModel.ResourceTypeIdRange.Returns((AllergyIntolerance, Procedure));

            foreach (FieldInfo fieldInfo in typeof(TypeConstraintVisitorTests).GetFields(BindingFlags.NonPublic | BindingFlags.Static).Where(fi => fi.IsLiteral && !fi.IsInitOnly))
            {
                sqlServerFhirModel.GetResourceTypeId(fieldInfo.Name).Returns((byte)fieldInfo.GetValue(null));
            }

            return sqlServerFhirModel;
        }

        private static StringExpression Token(string parameterValue) => StringEquals(FieldName.TokenCode, null, parameterValue, false);
    }
}
