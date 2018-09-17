// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Superpower.Model;
using Xunit;

namespace Microsoft.Health.Fhir.AccessControl.TemplateExpressions.UnitTests
{
    public class DiagnosticTests
    {
        [Fact]
        public void GivenTwoEquivalentdianostics_WhenComparing_ThenTheyShouldBeEqual()
        {
            var d1 = new TemplateExpressionDiagnostic(new TextSpan("s", new Position(1, 1, 1), 5), "hello");
            var d2 = new TemplateExpressionDiagnostic(new TextSpan("s", new Position(1, 1, 1), 5), "hello");
            d1.ShouldBe(d2);
            ((object)d1).ShouldBe(d2);
            (d1 == d2).ShouldBeTrue();
            (d1 != d2).ShouldBeFalse();
            d1.GetHashCode().ShouldBe(d2.GetHashCode());
        }

        [Fact]
        public void GivenTwoDiagnosticsWithDifferentTextSpans_WhenComparing_ThenTheyShouldNotBeEqual()
        {
            var d1 = new TemplateExpressionDiagnostic(new TextSpan("s", new Position(2, 1, 1), 5), "hello");
            var d2 = new TemplateExpressionDiagnostic(new TextSpan("s", new Position(100, 1, 100), 5), "hello");
            d1.ShouldNotBe(d2);
            ((object)d1).ShouldNotBe(d2);
            (d1 == d2).ShouldBeFalse();
            (d1 != d2).ShouldBeTrue();
            d1.GetHashCode().ShouldNotBe(d2.GetHashCode());
        }

        [Fact]
        public void GivenTwoDiagnosticsWithDifferentMessages_WhenComparing_ThenTheyShouldNotBeEqual()
        {
            var d1 = new TemplateExpressionDiagnostic(new TextSpan("s", new Position(2, 1, 1), 5), "hello");
            var d2 = new TemplateExpressionDiagnostic(new TextSpan("s", new Position(2, 1, 1), 5), "goodbye");
            d1.ShouldNotBe(d2);
            ((object)d1).ShouldNotBe(d2);
            (d1 == d2).ShouldBeFalse();
            (d1 != d2).ShouldBeTrue();
            d1.GetHashCode().ShouldNotBe(d2.GetHashCode());
        }
    }
}
