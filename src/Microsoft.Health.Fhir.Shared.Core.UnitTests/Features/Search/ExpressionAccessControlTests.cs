// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Access;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Context;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Expression = Microsoft.Health.Fhir.Core.Features.Search.Expressions.Expression;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ExpressionAccessControlTests
    {
        private static readonly SearchParameterInfo PatientParam = new SearchParameterInfo("patient", "patient");
        private static readonly SearchParameterInfo TargetParam = new SearchParameterInfo("target", "target");

        private static Expression Leaf() => Expression.SearchParameter(new SearchParameterInfo("code", "code"), Expression.StringEquals(FieldName.TokenCode, null, "foo", false));

        private static Expression HasObservation(Expression inner) =>
            Expression.Chained(new[] { "Observation" }, PatientParam, new[] { "Patient" }, true, inner);

        private static Expression HasProvenance(Expression inner) =>
            Expression.Chained(new[] { "Provenance" }, TargetParam, new[] { "Observation" }, true, inner);

        [Fact]
        public void GivenAHasSearchForAGrantedType_WhenChecked_ThenNoExceptionIsThrown()
        {
            var accessControl = CreateAccessControl();

            accessControl.CheckAndRaiseAccessExceptions(HasObservation(Leaf()), Scopes("Patient", "Observation"));
        }

        [Fact]
        public void GivenAHasSearchForAnUnGrantedType_WhenChecked_ThenSearchIsRejected()
        {
            var accessControl = CreateAccessControl();

            Assert.Throws<InvalidSearchOperationException>(
                () => accessControl.CheckAndRaiseAccessExceptions(HasObservation(Leaf()), Scopes("Patient")));
        }

        [Fact]
        public void GivenANestedHasSearchForAnUnGrantedType_WhenChecked_ThenSearchIsRejected()
        {
            var accessControl = CreateAccessControl();
            Expression nested = HasObservation(HasProvenance(Leaf()));

            Assert.Throws<InvalidSearchOperationException>(
                () => accessControl.CheckAndRaiseAccessExceptions(nested, Scopes("Patient", "Observation")));
        }

        [Fact]
        public void GivenANestedHasSearchForGrantedTypes_WhenChecked_ThenNoExceptionIsThrown()
        {
            var accessControl = CreateAccessControl();
            Expression nested = HasObservation(HasProvenance(Leaf()));

            accessControl.CheckAndRaiseAccessExceptions(nested, Scopes("Patient", "Observation", "Provenance"));
        }

        [Fact]
        public void GivenAHasSearchInsideAnOrGroup_WhenChecked_ThenSearchIsRejected()
        {
            var accessControl = CreateAccessControl();
            Expression orGroup = Expression.Or(Leaf(), HasObservation(Leaf()));
            Expression expression = Expression.And(Leaf(), orGroup);

            Assert.Throws<InvalidSearchOperationException>(
                () => accessControl.CheckAndRaiseAccessExceptions(expression, Scopes("Patient")));
        }

        [Fact]
        public void GivenAHasSearchInsideANotExpression_WhenChecked_ThenSearchIsRejected()
        {
            var accessControl = CreateAccessControl();
            Expression expression = Expression.Not(HasObservation(Leaf()));

            Assert.Throws<InvalidSearchOperationException>(
                () => accessControl.CheckAndRaiseAccessExceptions(expression, Scopes("Patient")));
        }

        [Fact]
        public void GivenAForwardChainToAnUnGrantedType_WhenChecked_ThenSearchIsRejected()
        {
            var accessControl = CreateAccessControl();
            Expression chain = Expression.Chained(new[] { "Observation" }, PatientParam, new[] { "Patient" }, false, Leaf());

            Assert.Throws<InvalidSearchOperationException>(
                () => accessControl.CheckAndRaiseAccessExceptions(chain, Scopes("Observation")));
        }

        [Fact]
        public void GivenAChainInsideAScopeInjectedExpression_WhenChecked_ThenItIsNotCheckedAgainstCallerScopes()
        {
            var accessControl = CreateAccessControl();
            MultiaryExpression scopeExpression = Expression.And(HasObservation(Leaf()));
            scopeExpression.IsSmartV2UnionExpressionForScopesSearchParameters = true;
            Expression expression = Expression.And(Leaf(), scopeExpression);

            accessControl.CheckAndRaiseAccessExceptions(expression, Scopes("Patient"));
        }

        [Fact]
        public void GivenFineGrainedAccessControlIsNotApplied_WhenChecked_ThenNoExceptionIsThrown()
        {
            var accessControl = CreateAccessControl(applyFineGrainedAccessControl: false);

            accessControl.CheckAndRaiseAccessExceptions(HasObservation(Leaf()), Scopes("Patient"));
        }

        private static ScopeRestriction[] Scopes(params string[] resourceTypes)
        {
            var scopes = new ScopeRestriction[resourceTypes.Length];
            for (int i = 0; i < resourceTypes.Length; i++)
            {
                scopes[i] = new ScopeRestriction(resourceTypes[i], DataActions.Read, "patient");
            }

            return scopes;
        }

        private static ExpressionAccessControl CreateAccessControl(bool applyFineGrainedAccessControl = true)
        {
            var requestContext = new DefaultFhirRequestContext
            {
                AccessControlContext = new AccessControlContext
                {
                    ApplyFineGrainedAccessControl = applyFineGrainedAccessControl,
                },
            };

            RequestContextAccessor<IFhirRequestContext> accessor = requestContext.SetupAccessor();
            return new ExpressionAccessControl(accessor);
        }
    }
}
