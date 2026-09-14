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
                    string code = callInfo.ArgAt<string>(1);

                    if (string.Equals(code, UnavailableParameterCode, StringComparison.Ordinal))
                    {
                        throw new SearchParameterNotSupportedException(callInfo.ArgAt<string[]>(0).First(), code);
                    }

                    return new TestExpression($"{code}={callInfo.ArgAt<string>(2)}");
                });
            expressionParser.ParseInclude(Arg.Any<string[]>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<IReadOnlyCollection<string>>())
                .Returns((IncludeExpression)null);

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
                throw new NotImplementedException();
        }
    }
}
