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
        private const short AllergyIntolerance = 1;
        private const short Claim = 2;
        private const short Condition = 3;
        private const short Device = 4;
        private const short DiagnosticReport = 5;
        private const short Encounter = 6;
        private const short Immunization = 7;
        private const short Observation = 8;
        private const short Patient = 9;
        private const short Procedure = 10;

        private static readonly short[] AllTypes = Enumerable.Range(AllergyIntolerance, Procedure - AllergyIntolerance + 1).Select(i => (short)i).ToArray();
        private static readonly ISqlServerFhirModel FhirModel = CreateFhirModel();
        private static readonly SearchParameterInfo TypeParameter = new(SearchParameterNames.ResourceType, SearchParameterNames.ResourceType);
        private static readonly SearchParameterInfo IdParameter = new(SearchParameterNames.Id, SearchParameterNames.Id);

        public static readonly TheoryData<Expression, short[]> Data = new()
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
        public void GivenAnExpression_WhenVisited_DeterminesTheCorrectAllowedTypes(Expression expression, short[] expectedTypeIds)
        {
            var visitor = new TypeConstraintVisitor();

            var result = visitor.Visit(expression, FhirModel);

            AssertAllowed(result, expectedTypeIds);
        }

        private static void AssertAllowed((short? singleAllowedResourceTypeId, BitArray allAllowedTypes) result, params short[] expectedIds)
        {
            expectedIds ??= Array.Empty<short>();

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

            for (short i = FhirModel.ResourceTypeIdRange.lowestId; i <= FhirModel.ResourceTypeIdRange.highestId; i++)
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
                sqlServerFhirModel.GetResourceTypeId(fieldInfo.Name).Returns((short)fieldInfo.GetValue(null));
            }

            return sqlServerFhirModel;
        }

        private static StringExpression Token(string parameterValue) => StringEquals(FieldName.TokenCode, null, parameterValue, false);
    }
}
