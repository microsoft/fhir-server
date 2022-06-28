// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Reflection;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using static Microsoft.Health.Fhir.Core.Features.Search.Expressions.Expression;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    [Trait(Traits.Category, Categories.Search)]
    [Trait(Traits.Category, Categories.CompartmentSearch)]
    public class PartitionEliminationRewriterTests
    {
        private const short AllergyIntolerance = 1;
        private const short Claim = 2;
        private const short Condition = 3;
        private const short Device = 4;
        private const short DiagnosticReport = 5;

        private static readonly short[] AllTypes = Enumerable.Range(AllergyIntolerance, DiagnosticReport - AllergyIntolerance + 1).Select(i => (short)i).ToArray();
        private static readonly SearchParameterInfo TypeParameter = new(SearchParameterNames.ResourceType, SearchParameterNames.ResourceType);
        private static readonly SearchParameterInfo IdParameter = new(SearchParameterNames.Id, SearchParameterNames.Id);

        private readonly ISqlServerFhirModel _fhirModel;
        private ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private PartitionEliminationRewriter _rewriter;

        public PartitionEliminationRewriterTests()
        {
            _searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            _searchParameterDefinitionManager.GetSearchParameter(KnownResourceTypes.Resource, SearchParameterNames.ResourceType).Returns(TypeParameter);

            _fhirModel = Substitute.For<ISqlServerFhirModel>();
            _fhirModel.ResourceTypeIdRange.Returns((AllergyIntolerance, DiagnosticReport));

            foreach (FieldInfo fieldInfo in typeof(TypeConstraintVisitorTests).GetFields(BindingFlags.NonPublic | BindingFlags.Static).Where(fi => fi.IsLiteral && !fi.IsInitOnly))
            {
                short id = (short)fieldInfo.GetValue(null);
                _fhirModel.GetResourceTypeId(fieldInfo.Name).Returns(id);
                _fhirModel.GetResourceTypeName(id).Returns(fieldInfo.Name);
            }

            _rewriter = new PartitionEliminationRewriter(_fhirModel, new SchemaInformation(SchemaVersionConstants.PartitionedTables, SchemaVersionConstants.PartitionedTables), () => _searchParameterDefinitionManager);
        }

        [Fact]
        public void GivenACrossSystemQuery_WhenRewritten_GetsAllResourceTypes()
        {
            Expression rewritten = new SqlRootExpression(
                    Array.Empty<SearchParamTableExpression>(),
                    new[] { SearchParameter(IdParameter, Token("foo")) })
                .AcceptVisitor(_rewriter);

            Assert.Equal(
                "(SqlRoot (SearchParamTables:) (ResourceTable: (Param _id (StringEquals TokenCode 'foo')) (Param _type (TokenCode IN (AllergyIntolerance, Claim, Condition, Device, DiagnosticReport)))))",
                rewritten.ToString());
        }

        [Fact]
        public void GivenAnExpressionWithTypeConstraintWithoutAContinuationToken_WhenRewritten_RemainsTheSame()
        {
            var inputExpression = new SqlRootExpression(
                Array.Empty<SearchParamTableExpression>(),
                new[] { SearchParameter(TypeParameter, Token(nameof(Claim))) });

            Expression rewritten = inputExpression.AcceptVisitor(_rewriter);

            Assert.Same(inputExpression, rewritten);
        }

        [Fact]
        public void GivenAnExpressionWithTypeSingleTypeAndAContinuationToken_WhenRewritten_GetsResourceSurrogateIdExpression()
        {
            var inputExpression = new SqlRootExpression(
                Array.Empty<SearchParamTableExpression>(),
                new[] { SearchParameter(TypeParameter, Token(nameof(Claim))), SearchParameter(SqlSearchParameters.PrimaryKeyParameter, GreaterThan(SqlFieldName.PrimaryKey, null, new PrimaryKeyValue(Claim, 22))) });

            Expression rewritten = inputExpression.AcceptVisitor(_rewriter);

            Assert.Equal(
                "(SqlRoot (SearchParamTables:) (ResourceTable: (Param _type (StringEquals TokenCode 'Claim')) (Param _resourceSurrogateId (FieldGreaterThan 100 22))))",
                rewritten.ToString());
        }

        [Fact]
        public void GivenAnExpressionWithMultipleTypesAndAContinuationToken_WhenRewritten_GetsResourceSurrogateIdExpression()
        {
            var inputExpression = new SqlRootExpression(
                Array.Empty<SearchParamTableExpression>(),
                new[]
                {
                    SearchParameter(TypeParameter, Or(Token(nameof(Claim)), Token(nameof(Condition)), Token(nameof(Device)))),
                    SearchParameter(IdParameter, Token("foo")),
                    SearchParameter(SqlSearchParameters.PrimaryKeyParameter, GreaterThan(SqlFieldName.PrimaryKey, null, new PrimaryKeyValue(Condition, 22))),
                });

            Expression rewritten = inputExpression.AcceptVisitor(_rewriter);

            Assert.Equal(
                "(SqlRoot (SearchParamTables:) (ResourceTable: (Param _id (StringEquals TokenCode 'foo')) (Param _primaryKey (FieldGreaterThan 108 (PrimaryKeyRange (PrimaryKey 3 22) (Next 4))))))",
                rewritten.ToString());
        }

        [Fact]
        public void GivenAnExpressionWithMultipleTypesAndAContinuationTokenInDescendingOrder_WhenRewritten_GetsResourceSurrogateIdExpression()
        {
            var inputExpression = new SqlRootExpression(
                Array.Empty<SearchParamTableExpression>(),
                new[]
                {
                    SearchParameter(TypeParameter, Or(Token(nameof(Claim)), Token(nameof(Condition)), Token(nameof(Device)))),
                    SearchParameter(IdParameter, Token("foo")),
                    SearchParameter(SqlSearchParameters.PrimaryKeyParameter, LessThan(SqlFieldName.PrimaryKey, null, new PrimaryKeyValue(Condition, 22))),
                });

            Expression rewritten = inputExpression.AcceptVisitor(_rewriter);

            Assert.Equal(
                "(SqlRoot (SearchParamTables:) (ResourceTable: (Param _id (StringEquals TokenCode 'foo')) (Param _primaryKey (FieldLessThan 108 (PrimaryKeyRange (PrimaryKey 3 22) (Next 2))))))",
                rewritten.ToString());
        }

        private static StringExpression Token(string parameterValue) => StringEquals(FieldName.TokenCode, null, parameterValue, false);
    }
}
