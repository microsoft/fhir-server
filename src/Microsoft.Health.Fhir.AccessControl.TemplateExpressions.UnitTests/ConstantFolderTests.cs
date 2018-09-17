// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Ast;
using Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Evaluation;
using Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Parser;
using Shouldly;
using Superpower.Model;
using Xunit;

namespace Microsoft.Health.Fhir.AccessControl.TemplateExpressions.UnitTests
{
    public class ConstantFolderTests
    {
        [Theory]
        [InlineData("{'hello'}", "'hello'")]
        [InlineData("{'hello'}{'hello'}", "'hellohello'")]
        [InlineData("{'hello{'hello'}'}", "'hellohello'")]
        [InlineData("{'hello'}{myMethod('hello')}", "(concat 'hello' (call myMethod 'hello'))")]
        [InlineData("{5}", "'5'")]
        [InlineData("a{5}b{'55{3}'}", "'a5b553'")]
        public void GivenAValidExpression_WhenFoldingConstants_SimplifiesStrings(string input, string expectedParsedAst)
        {
            var diagnostics = new TemplateExpressionDiagnosticCollection();
            TemplateExpression expression = TemplateExpressionParser.Parse(input, diagnostics);
            expression = expression.Accept(ConstantFolder.Instance, Unit.Value);
            diagnostics.ShouldBeEmpty();
            expression.Accept(TestStringTemplateExpressionVisitor.Instance, new StringBuilder()).ToString().ShouldBe(expectedParsedAst);
        }
    }
}
