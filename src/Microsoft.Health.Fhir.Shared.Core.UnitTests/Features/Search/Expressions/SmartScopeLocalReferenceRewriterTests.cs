// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Expression = Microsoft.Health.Fhir.Core.Features.Search.Expressions.Expression;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Expressions
{
    /// <summary>
    /// Parses reference values with the production parsers, so the tests cover the expression shapes that SMART scope
    /// search parameters actually produce.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SmartScopeLocalReferenceRewriterTests
    {
        private const string LocalBase = "https://localhost/fhir/";
        private const string ExternalBase = "https://external.fhir.invalid/r4/";

        private static readonly SearchParameterInfo SubjectParameter = new SearchParameterInfo(
            "subject",
            "subject",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"),
            targetResourceTypes: new[] { KnownResourceTypes.Patient, KnownResourceTypes.Group });

        private readonly SearchParameterExpressionParser _parser;

        public SmartScopeLocalReferenceRewriterTests()
        {
            var requestContext = Substitute.For<IFhirRequestContext>();
            requestContext.BaseUri.Returns(new Uri(LocalBase));
            var requestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            requestContextAccessor.RequestContext.Returns(requestContext);

            var instanceConfiguration = Substitute.For<IFhirServerInstanceConfiguration>();
            instanceConfiguration.BaseUri.Returns(new Uri(LocalBase));

            _parser = new SearchParameterExpressionParser(
                new ReferenceSearchValueParser(requestContextAccessor, instanceConfiguration, ResourceIdPolicy.Standard));
        }

        [Theory]
        [InlineData(null, "Patient/A", LocalBase + "Patient/A")]
        [InlineData(null, "Patient/A,Group/B", LocalBase + "Patient/A," + LocalBase + "Group/B")]
        [InlineData("Patient", "A", LocalBase + "Patient/A")]
        public void GivenARelativeReference_WhenRewritten_ThenItMatchesOnlyTheLocalResource(string typeModifier, string relativeValue, string absoluteLocalValue)
        {
            SearchModifier modifier = typeModifier == null ? null : new SearchModifier(SearchModifierCode.Type, typeModifier);
            Expression relative = Parse(relativeValue, modifier);

            Expression rewritten = Rewrite(relative);

            // Before the rewrite the predicate ignores the base URI, which is how the scope matched other servers.
            Assert.DoesNotContain(nameof(FieldName.ReferenceBaseUri), relative.ToString(), StringComparison.Ordinal);
            Assert.Equal(Parse(absoluteLocalValue).ToString(), rewritten.ToString());
        }

        [Fact]
        public void GivenAnIdOnlyReference_WhenRewritten_ThenTheBaseUriMustBeMissing()
        {
            Expression rewritten = Rewrite(Parse("A"));

            Assert.Equal(
                "(Param subject (And (MissingField ReferenceBaseUri) (StringEquals ReferenceResourceId 'A')))",
                rewritten.ToString());
        }

        [Theory]
        [InlineData(LocalBase + "Patient/A")]
        [InlineData(ExternalBase + "Patient/A")]
        public void GivenAnAbsoluteReference_WhenRewritten_ThenItIsUnchanged(string absoluteValue)
        {
            Expression expression = Parse(absoluteValue);

            Assert.Same(expression, Rewrite(expression));
        }

        [Fact]
        public void GivenANegatedReference_WhenRewritten_ThenItIsUnchanged()
        {
            // NOT (local Patient/A) would also admit external Patient/A, widening the scope.
            Expression expression = Parse("Patient/A,Patient/B", new SearchModifier(SearchModifierCode.Not));

            Assert.Same(expression, Rewrite(expression));
        }

        [Fact]
        public void GivenANonReferencePredicate_WhenRewritten_ThenItIsUnchanged()
        {
            var codeParameter = new SearchParameterInfo("code", "code", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/clinical-code"));
            Expression expression = _parser.Parse(codeParameter, null, "http://loinc.org|1234-5");

            Assert.Same(expression, Rewrite(expression));
        }

        [Fact]
        public void GivenACompositeWithAReferenceComponent_WhenRewritten_ThenOnlyTheReferenceComponentIsRestricted()
        {
            Expression expression = Expression.And(
                Expression.StringEquals(FieldName.TokenCode, 0, "code-1", false),
                Expression.And(
                    Expression.StringEquals(FieldName.ReferenceResourceType, 1, KnownResourceTypes.Patient, false),
                    Expression.StringEquals(FieldName.ReferenceResourceId, 1, "A", false)));

            Assert.Equal(
                "(And (StringEquals [0].TokenCode 'code-1') (And (MissingField [1].ReferenceBaseUri) (StringEquals [1].ReferenceResourceType 'Patient') (StringEquals [1].ReferenceResourceId 'A')))",
                Rewrite(expression).ToString());
        }

        private static Expression Rewrite(Expression expression) =>
            expression.AcceptVisitor(SmartScopeLocalReferenceRewriter.Instance, null);

        private Expression Parse(string value, SearchModifier modifier = null) =>
            _parser.Parse(SubjectParameter, modifier, value);
    }
}
