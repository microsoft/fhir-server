// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Rest;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Access;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Context;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Expression = Microsoft.Health.Fhir.Core.Features.Search.Expressions.Expression;
using SortOrder = Microsoft.Health.Fhir.Core.Features.Search.SortOrder;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    /// <summary>
    /// Verifies that SMART clinical scope constraints fail closed when the search parameter they depend on is
    /// unavailable. Dropping such a constraint silently widens the caller's effective scope, so the request must
    /// be denied rather than answered with a superset of the permitted data.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchOptionsFactorySmartScopeTests
    {
        private const string UnavailableParameterCode = "patient";
        private const string AvailableParameterCode = "code";
        private const string ConcreteOnlyParameterCode = "status";
        private const string ObservationOnlyParameterCode = "value-quantity";
        private const string ReferenceParameterCode = "subject";
        private const string LocalReferenceA = "(Param subject (And (MissingField ReferenceBaseUri) (StringEquals ReferenceResourceType 'Patient') (StringEquals ReferenceResourceId 'A')))";
        private const string AnyBaseReferenceA = "(Param subject (And (StringEquals ReferenceResourceType 'Patient') (StringEquals ReferenceResourceId 'A')))";

        [Fact]
        public void GivenResourceSpecificScopeWithUnavailableParameter_WhenCreated_ThenRequestIsDenied()
        {
            SearchOptionsFactory factory = CreateFactory(
                new ScopeRestriction(KnownResourceTypes.Observation, DataActions.Read, "patient", CreateSearchParams((UnavailableParameterCode, "patient-B"))));

            InvalidSearchOperationException exception = Assert.Throws<InvalidSearchOperationException>(
                () => factory.Create(KnownResourceTypes.Observation, queryParameters: null, onlyIds: false, isIncludesOperation: false));

            Assert.Contains(UnavailableParameterCode, exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenResourceSpecificScopeWithUnavailableParameterAndStrictHandling_WhenCreated_ThenRequestIsDenied()
        {
            // The denial is an authorization decision, so it must not depend on the caller's Prefer header.
            SearchOptionsFactory factory = CreateFactory(
                strictHandling: true,
                scopeRestrictions: new ScopeRestriction(KnownResourceTypes.Observation, DataActions.Read, "patient", CreateSearchParams((UnavailableParameterCode, "patient-B"))));

            Assert.Throws<InvalidSearchOperationException>(
                () => factory.Create(KnownResourceTypes.Observation, queryParameters: null, onlyIds: false, isIncludesOperation: false));
        }

        [Fact]
        public void GivenWildcardScopeWithUnavailableParameter_WhenCreated_ThenRequestIsDenied()
        {
            // A wildcard scope constraint is merged into the caller's query, where an unavailable parameter is
            // normally demoted to a warning. That leniency may not apply to an authorization predicate.
            SearchOptionsFactory factory = CreateFactory(
                new ScopeRestriction(KnownResourceTypes.All, DataActions.Read, "patient", CreateSearchParams((UnavailableParameterCode, "patient-B"))));

            InvalidSearchOperationException exception = Assert.Throws<InvalidSearchOperationException>(
                () => factory.Create(KnownResourceTypes.Observation, queryParameters: null, onlyIds: false, isIncludesOperation: false));

            Assert.Contains(UnavailableParameterCode, exception.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void GivenAnUnenforceableScopeAndAnUnrestrictedScopeForTheSameType_WhenCreated_ThenTheUnrestrictedScopeStillAuthorizes(bool unenforceableScopeFirst)
        {
            // Scopes are alternatives. The unrestricted scope already grants every Observation, so withdrawing the
            // unenforceable one loses nothing and the request must not be denied — regardless of scope order.
            var unenforceable = new ScopeRestriction(KnownResourceTypes.Observation, DataActions.Read, "patient", CreateSearchParams((UnavailableParameterCode, "patient-B")));
            var unrestricted = new ScopeRestriction(KnownResourceTypes.Observation, DataActions.Read, "patient");

            SearchOptionsFactory factory = unenforceableScopeFirst
                ? CreateFactory(unenforceable, unrestricted)
                : CreateFactory(unrestricted, unenforceable);

            SearchOptions options = factory.Create(KnownResourceTypes.Observation, queryParameters: null, onlyIds: false, isIncludesOperation: false);

            string expression = options.Expression.ToString();
            Assert.DoesNotContain($"{UnavailableParameterCode}=", expression, StringComparison.Ordinal);
            Assert.Contains(KnownResourceTypes.Observation, expression, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenAnUnenforceableScopeAndAnEnforceableScopeForTheSameType_WhenCreated_ThenOnlyTheEnforceableScopeIsApplied()
        {
            SearchOptionsFactory factory = CreateFactory(
                new ScopeRestriction(KnownResourceTypes.Observation, DataActions.Read, "patient", CreateSearchParams((UnavailableParameterCode, "patient-B"))),
                new ScopeRestriction(KnownResourceTypes.Observation, DataActions.Read, "patient", CreateSearchParams((AvailableParameterCode, "loinc-1"))));

            SearchOptions options = factory.Create(KnownResourceTypes.Observation, queryParameters: null, onlyIds: false, isIncludesOperation: false);

            string expression = options.Expression.ToString();
            Assert.Contains($"{AvailableParameterCode}=loinc-1", expression, StringComparison.Ordinal);
            Assert.DoesNotContain($"{UnavailableParameterCode}=", expression, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenAnUnenforceableScopeOnlyForAnUnrequestedType_WhenCreated_ThenTheRequestIsNotDenied()
        {
            SearchOptionsFactory factory = CreateFactory(
                new ScopeRestriction("Condition", DataActions.Read, "patient", CreateSearchParams((UnavailableParameterCode, "patient-B"))),
                new ScopeRestriction(KnownResourceTypes.Observation, DataActions.Read, "patient"));

            SearchOptions options = factory.Create(KnownResourceTypes.Observation, queryParameters: null, onlyIds: false, isIncludesOperation: false);

            Assert.NotNull(options.Expression);
        }

        [Fact]
        public void GivenASystemLevelSearchAndATypeWhoseOnlyScopeIsUnenforceable_WhenCreated_ThenRequestIsDenied()
        {
            // A system-level search requests every scoped type, so Observation is requested and has nothing left
            // that authorizes it. Returning the Condition results alone would be a silently partial answer.
            SearchOptionsFactory factory = CreateFactory(
                new ScopeRestriction(KnownResourceTypes.Observation, DataActions.Read, "patient", CreateSearchParams((UnavailableParameterCode, "patient-B"))),
                new ScopeRestriction("Condition", DataActions.Read, "patient"));

            InvalidSearchOperationException exception = Assert.Throws<InvalidSearchOperationException>(
                () => factory.Create(resourceType: null, queryParameters: null, onlyIds: false, isIncludesOperation: false));

            Assert.Contains(UnavailableParameterCode, exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenResourceSpecificScopeWithAvailableParameter_WhenCreated_ThenScopePredicateIsApplied()
        {
            SearchOptionsFactory factory = CreateFactory(
                new ScopeRestriction(KnownResourceTypes.Observation, DataActions.Read, "patient", CreateSearchParams((AvailableParameterCode, "loinc-1"))));

            SearchOptions options = factory.Create(KnownResourceTypes.Observation, queryParameters: null, onlyIds: false, isIncludesOperation: false);

            Assert.Contains($"{AvailableParameterCode}=loinc-1", options.Expression.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public void GivenWildcardScopeWithAvailableParameter_WhenCreated_ThenScopePredicateIsApplied()
        {
            SearchOptionsFactory factory = CreateFactory(
                new ScopeRestriction(KnownResourceTypes.All, DataActions.Read, "patient", CreateSearchParams((AvailableParameterCode, "loinc-1"))));

            SearchOptions options = factory.Create(KnownResourceTypes.Observation, queryParameters: null, onlyIds: false, isIncludesOperation: false);

            Assert.Contains($"{AvailableParameterCode}=loinc-1", options.Expression.ToString(), StringComparison.Ordinal);
            Assert.Empty(options.UnsupportedSearchParams);
        }

        [Theory]
        [InlineData("_include", "DiagnosticReport:result", false)]
        [InlineData("_revinclude", "Observation:subject", true)]
        public void GivenWildcardScopeWithAvailableParameterAndInclude_WhenCreated_ThenSmartScopeUnionIsApplied(
            string includeParameterName,
            string includeValue,
            bool reversed)
        {
            SearchOptionsFactory factory = CreateFactory(
                new ScopeRestriction(KnownResourceTypes.All, DataActions.Read, "patient", CreateSearchParams(("_tag", "allowed"))));

            SearchOptions options = factory.Create(
                reversed ? KnownResourceTypes.Patient : KnownResourceTypes.DiagnosticReport,
                new[] { Tuple.Create(includeParameterName, includeValue) },
                onlyIds: false,
                isIncludesOperation: false);

            var multiary = Assert.IsType<MultiaryExpression>(options.Expression);
            UnionExpression smartUnion = Assert.Single(multiary.Expressions.OfType<UnionExpression>());
            Assert.True(smartUnion.IsSmartV2UnionExpressionForScopesSearchParameters);
            Assert.Contains("_tag=allowed", smartUnion.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public void GivenWildcardScopeWithUnavailableParameterAndRevInclude_WhenCreated_ThenRequestIsDenied()
        {
            SearchOptionsFactory factory = CreateFactory(
                new ScopeRestriction(KnownResourceTypes.All, DataActions.Read, "patient", CreateSearchParams((UnavailableParameterCode, "patient-B"))));

            InvalidSearchOperationException exception = Assert.Throws<InvalidSearchOperationException>(
                () => factory.Create(
                    KnownResourceTypes.Patient,
                    new[] { Tuple.Create("_revinclude", "Observation:subject") },
                    onlyIds: false,
                    isIncludesOperation: false));

            Assert.Contains(UnavailableParameterCode, exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenWildcardScopeWithParameterUnsupportedOnDomainResourceButSupportedOnConcreteTypes_WhenCreatedWithInclude_ThenConcreteTypeFallbackSucceeds()
        {
            // Clinical search parameters (such as "status") are defined on concrete resource types rather than
            // DomainResource. Parsing against DomainResource fails first, and the concrete-type fallback builds a
            // typed union leg for each required resource type (DiagnosticReport and Observation).
            SearchOptionsFactory factory = CreateFactory(
                new ScopeRestriction(KnownResourceTypes.All, DataActions.Read, "patient", CreateSearchParams((ConcreteOnlyParameterCode, "final"))));

            SearchOptions options = factory.Create(
                KnownResourceTypes.DiagnosticReport,
                new[] { Tuple.Create("_include", "DiagnosticReport:result") },
                onlyIds: false,
                isIncludesOperation: false);

            var multiary = Assert.IsType<MultiaryExpression>(options.Expression);
            UnionExpression smartUnion = Assert.Single(multiary.Expressions.OfType<UnionExpression>());
            Assert.True(smartUnion.IsSmartV2UnionExpressionForScopesSearchParameters);
            Assert.Equal(2, smartUnion.Expressions.Count);
            string unionText = smartUnion.ToString();
            Assert.Contains(KnownResourceTypes.DiagnosticReport, unionText, StringComparison.Ordinal);
            Assert.Contains(KnownResourceTypes.Observation, unionText, StringComparison.Ordinal);
            Assert.Contains($"{ConcreteOnlyParameterCode}=final", unionText, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenWildcardScopeWithParameterSupportedOnOnlyOneRequiredType_WhenOtherTypeHasNoFallbackScope_ThenRequestIsDenied()
        {
            // Observation?_include=Observation:subject requires both Observation and Patient. When the wildcard
            // scope parameter is only valid on Observation and no other scope grants Patient, the request must be denied.
            SearchOptionsFactory factory = CreateFactory(
                new ScopeRestriction(KnownResourceTypes.All, DataActions.Read, "patient", CreateSearchParams((ObservationOnlyParameterCode, "5"))));

            InvalidSearchOperationException exception = Assert.Throws<InvalidSearchOperationException>(
                () => factory.Create(
                    KnownResourceTypes.Observation,
                    new[] { Tuple.Create("_include", "Observation:subject") },
                    onlyIds: false,
                    isIncludesOperation: false));

            Assert.Contains(ObservationOnlyParameterCode, exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenWildcardScopeWithParameterSupportedOnOnlyOneRequiredType_WhenOtherTypeHasExplicitScope_ThenRequestSucceeds()
        {
            // When the wildcard scope fallback covers Observation and a separate Patient scope covers Patient,
            // every required resource type is authorized.
            SearchOptionsFactory factory = CreateFactory(
                new ScopeRestriction(KnownResourceTypes.All, DataActions.Read, "patient", CreateSearchParams((ObservationOnlyParameterCode, "5"))),
                new ScopeRestriction(KnownResourceTypes.Patient, DataActions.Read, "patient"));

            SearchOptions options = factory.Create(
                KnownResourceTypes.Observation,
                new[] { Tuple.Create("_include", "Observation:subject") },
                onlyIds: false,
                isIncludesOperation: false);

            var multiary = Assert.IsType<MultiaryExpression>(options.Expression);
            UnionExpression smartUnion = Assert.Single(multiary.Expressions.OfType<UnionExpression>());
            Assert.Equal(2, smartUnion.Expressions.Count);
            string unionText = smartUnion.ToString();
            Assert.Contains($"{ObservationOnlyParameterCode}=5", unionText, StringComparison.Ordinal);
            Assert.Contains(KnownResourceTypes.Patient, unionText, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenMultiParameterScopeWhereLaterParameterFails_WhenCreated_ThenPartiallyParsedScopeDoesNotSurvive()
        {
            // If an earlier parameter in a scope succeeds ("code=loinc-1") and a later parameter in the same scope
            // fails ("patient=patient-B"), the entire scope must be withdrawn rather than surviving with only the
            // first parameter applied.
            SearchOptionsFactory factory = CreateFactory(
                new ScopeRestriction(
                    KnownResourceTypes.All,
                    DataActions.Read,
                    "patient",
                    CreateSearchParams((AvailableParameterCode, "loinc-1"), (UnavailableParameterCode, "patient-B"))),
                new ScopeRestriction(KnownResourceTypes.Observation, DataActions.Read, "patient"),
                new ScopeRestriction(KnownResourceTypes.Patient, DataActions.Read, "patient"));

            SearchOptions options = factory.Create(
                KnownResourceTypes.Patient,
                new[] { Tuple.Create("_revinclude", "Observation:subject") },
                onlyIds: false,
                isIncludesOperation: false);

            Assert.DoesNotContain($"{AvailableParameterCode}=loinc-1", options.Expression.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public void GivenCallerSuppliedUnavailableParameter_WhenCreated_ThenOrdinaryLenientHandlingIsPreserved()
        {
            // Only the scope's own predicates fail closed. An unsupported parameter the caller typed keeps the
            // existing lenient behavior of being reported back rather than failing the request.
            SearchOptionsFactory factory = CreateFactory(
                new ScopeRestriction(KnownResourceTypes.Observation, DataActions.Read, "patient", CreateSearchParams((AvailableParameterCode, "loinc-1"))));

            SearchOptions options = factory.Create(
                KnownResourceTypes.Observation,
                new[] { Tuple.Create(UnavailableParameterCode, "patient-B") },
                onlyIds: false,
                isIncludesOperation: false);

            Assert.Contains(options.UnsupportedSearchParams, p => p.Item1 == UnavailableParameterCode);
        }

        [Fact]
        public void GivenCallerSuppliedParameterSharingTheScopeParameterName_WhenCreated_ThenOnlyTheScopeConstraintFailsClosed()
        {
            // The dropped-constraint check matches the exact name/value pair the scope injected, so a different
            // value of the same parameter typed by the caller is not mistaken for the scope's predicate.
            SearchOptionsFactory factory = CreateFactory(
                new ScopeRestriction(KnownResourceTypes.Observation, DataActions.Read, "patient", CreateSearchParams((AvailableParameterCode, "loinc-1"))));

            SearchOptions options = factory.Create(
                KnownResourceTypes.Observation,
                new[] { Tuple.Create(UnavailableParameterCode, "some-other-value") },
                onlyIds: false,
                isIncludesOperation: false);

            Assert.Contains(options.UnsupportedSearchParams, p => p.Item2 == "some-other-value");
        }

        [Theory]
        [InlineData(KnownResourceTypes.Observation, KnownResourceTypes.Observation, null)]
        [InlineData(KnownResourceTypes.All, KnownResourceTypes.Observation, null)]
        [InlineData(KnownResourceTypes.All, KnownResourceTypes.Patient, "Observation:subject")]
        public void GivenScopeWithRelativeReference_WhenCreated_ThenScopeMatchesOnlyLocalReferences(
            string scopeResourceType,
            string searchResourceType,
            string revInclude)
        {
            // Covers the three ways a scope constraint reaches the query: a resource-specific scope, a wildcard scope
            // merged into the caller's query, and a wildcard scope built as a union leg because of _revinclude.
            SearchOptionsFactory factory = CreateFactory(
                new ScopeRestriction(scopeResourceType, DataActions.Read, "system", CreateSearchParams((ReferenceParameterCode, "Patient/A"))));

            SearchOptions options = factory.Create(
                searchResourceType,
                revInclude == null ? null : new[] { Tuple.Create("_revinclude", revInclude) },
                onlyIds: false,
                isIncludesOperation: false);

            Assert.Contains(LocalReferenceA, options.Expression.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(AnyBaseReferenceA, options.Expression.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public void GivenCallerSuppliedRelativeReference_WhenCreatedWithAWildcardScope_ThenOnlyTheScopeConstraintIsRestricted()
        {
            // A reference the caller searches for is not an authorization predicate, so it keeps matching any base.
            SearchOptionsFactory factory = CreateFactory(
                new ScopeRestriction(KnownResourceTypes.All, DataActions.Read, "system", CreateSearchParams((ReferenceParameterCode, "Patient/A"))));

            SearchOptions options = factory.Create(
                KnownResourceTypes.Observation,
                new[] { Tuple.Create(ReferenceParameterCode, "Patient/B") },
                onlyIds: false,
                isIncludesOperation: false);

            string expression = options.Expression.ToString();
            Assert.Contains(LocalReferenceA, expression, StringComparison.Ordinal);
            Assert.Contains(
                "(Param subject (And (StringEquals ReferenceResourceType 'Patient') (StringEquals ReferenceResourceId 'B')))",
                expression,
                StringComparison.Ordinal);
        }

        private static SearchParams CreateSearchParams(params (string Name, string Value)[] parameters)
        {
            var searchParams = new SearchParams();

            foreach ((string name, string value) in parameters)
            {
                searchParams.Add(name, value);
            }

            return searchParams;
        }

        private static SearchOptionsFactory CreateFactory(params ScopeRestriction[] scopeRestrictions)
        {
            return CreateFactory(strictHandling: false, scopeRestrictions);
        }

        private static SearchOptionsFactory CreateFactory(bool strictHandling, params ScopeRestriction[] scopeRestrictions)
        {
            // The expression parser resolves through the searchable definition manager, so a disabled, pending
            // delete, or reindexing parameter surfaces here as SearchParameterNotSupportedException.
            var expressionParser = Substitute.For<IExpressionParser>();
            expressionParser.Parse(Arg.Any<string[]>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(callInfo =>
                {
                    string targetType = callInfo.ArgAt<string[]>(0).First();
                    string code = callInfo.ArgAt<string>(1);

                    if (string.Equals(code, UnavailableParameterCode, StringComparison.Ordinal))
                    {
                        throw new SearchParameterNotSupportedException(targetType, code);
                    }

                    if (string.Equals(code, ConcreteOnlyParameterCode, StringComparison.Ordinal) &&
                        string.Equals(targetType, KnownResourceTypes.DomainResource, StringComparison.Ordinal))
                    {
                        throw new SearchParameterNotSupportedException(targetType, code);
                    }

                    if (string.Equals(code, ObservationOnlyParameterCode, StringComparison.Ordinal) &&
                        !string.Equals(targetType, KnownResourceTypes.Observation, StringComparison.Ordinal))
                    {
                        throw new SearchParameterNotSupportedException(targetType, code);
                    }

                    if (string.Equals(code, ReferenceParameterCode, StringComparison.Ordinal))
                    {
                        // Same shape as the production parser for a relative reference such as Patient/A.
                        string[] reference = callInfo.ArgAt<string>(2).Split('/');
                        return Expression.SearchParameter(
                            new SearchParameterInfo(ReferenceParameterCode, ReferenceParameterCode, ValueSets.SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject")),
                            Expression.And(
                                Expression.StringEquals(FieldName.ReferenceResourceType, null, reference[0], false),
                                Expression.StringEquals(FieldName.ReferenceResourceId, null, reference[1], false)));
                    }

                    return new TestExpression($"{code}={callInfo.ArgAt<string>(2)}");
                });

            expressionParser.ParseInclude(Arg.Any<string[]>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<IReadOnlyCollection<string>>())
                .Returns(callInfo =>
                {
                    string includeQuery = callInfo.ArgAt<string>(1);
                    string sourceType = includeQuery.Split(':')[0];
                    bool isReversed = callInfo.ArgAt<bool>(2);
                    bool isIterate = callInfo.ArgAt<bool>(3);
                    var allowedTypes = callInfo.ArgAt<IReadOnlyCollection<string>>(4);
                    string targetType = isReversed || string.Equals(sourceType, KnownResourceTypes.Observation, StringComparison.Ordinal)
                        ? KnownResourceTypes.Patient
                        : KnownResourceTypes.Observation;
                    var referenceParam = new SearchParameterInfo(
                        "subject",
                        "subject",
                        ValueSets.SearchParamType.Reference,
                        new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"),
                        targetResourceTypes: new[] { targetType });
                    return new IncludeExpression(
                        callInfo.ArgAt<string[]>(0),
                        referenceParam,
                        sourceType,
                        targetType,
                        referencedTypes: null,
                        wildCard: false,
                        reversed: isReversed,
                        iterate: isIterate,
                        allowedResourceTypesByScope: allowedTypes);
                });

            var searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            searchParameterDefinitionManager.GetSearchParameter(Arg.Any<string>(), Arg.Any<string>())
                .Returns(new SearchParameterInfo(SearchParameterNames.ResourceType, SearchParameterNames.ResourceType, ValueSets.SearchParamType.Token, SearchParameterNames.ResourceTypeUri));

            var requestContext = new DefaultFhirRequestContext
            {
                AccessControlContext = new AccessControlContext
                {
                    ApplyFineGrainedAccessControl = true,
                    ApplyFineGrainedAccessControlWithSearchParameters = true,
                },
            };

            if (strictHandling)
            {
                requestContext.RequestHeaders = new Dictionary<string, StringValues>
                {
                    { KnownHeaders.Prefer, new StringValues("handling=strict") },
                };
            }

            foreach (ScopeRestriction restriction in scopeRestrictions)
            {
                requestContext.AccessControlContext.AllowedResourceActions.Add(restriction);
            }

            RequestContextAccessor<IFhirRequestContext> contextAccessor = requestContext.SetupAccessor();

            var sortingValidator = Substitute.For<ISortingValidator>();
            sortingValidator.ValidateSorting(Arg.Any<IReadOnlyList<(SearchParameterInfo, SortOrder)>>(), out Arg.Any<IReadOnlyList<string>>())
                .Returns(true);

            return new SearchOptionsFactory(
                expressionParser,
                () => searchParameterDefinitionManager,
                new OptionsWrapper<CoreFeatureConfiguration>(new CoreFeatureConfiguration()),
                contextAccessor,
                sortingValidator,
                new ExpressionAccessControl(contextAccessor),
                NullLogger<SearchOptionsFactory>.Instance);
        }

        private sealed class TestExpression : Expression
        {
            private readonly string _description;

            public TestExpression(string description)
            {
                _description = description;
            }

            public override string ToString() => _description;

            public override void AddValueInsensitiveHashCode(ref HashCode hashCode) => hashCode.Add(_description);

            public override bool ValueInsensitiveEquals(Expression other) =>
                other is TestExpression expression && string.Equals(expression._description, _description, StringComparison.Ordinal);

            public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context) =>
                this is TOutput self ? self : default;
        }
    }
}
