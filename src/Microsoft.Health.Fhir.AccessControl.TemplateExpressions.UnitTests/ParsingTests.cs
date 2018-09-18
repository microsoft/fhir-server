// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Text;
using Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Ast;
using Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Parser;
using Shouldly;
using Xunit;

namespace Microsoft.Health.Fhir.AccessControl.TemplateExpressions.UnitTests
{
    public class ParsingTests
    {
        [Theory]
        [InlineData("", "(concat '')")]
        [InlineData("a", "(concat 'a')")]
        [InlineData("\\'", "(concat '\\'')")]
        [InlineData("\\{", "(concat '{')")]
        [InlineData("{a()}", "(concat (call a))")]
        [InlineData("{22}", "(concat 22)")]
        [InlineData("{-22}", "(concat -22)")]
        [InlineData("{''}", "(concat (concat ''))")]
        [InlineData("{a('b', 'c')}", "(concat (call a (concat 'b') (concat 'c')))")]
        [InlineData("hello", "(concat 'hello')")]
        [InlineData(@"\'\'", @"(concat '\'\'')")]
        [InlineData("a{'b'}c", "(concat 'a' (concat 'b') 'c')")]
        [InlineData("a{callouter(callinner('b'))}c", "(concat 'a' (call callouter (call callinner (concat 'b'))) 'c')")]
        [InlineData("a1{m('b1{n('c1')}b2')}a2", "(concat 'a1' (call m (concat 'b1' (call n (concat 'c1')) 'b2')) 'a2')")]
        public void GivenASyntacticallyValidExpressionString_WhenParsed_CreatesTheExpectedExpressionTreeWithNoDiagnostics(string input, string expectedParsedAst)
        {
            var diagnostics = new TemplateExpressionDiagnosticCollection();
            TemplateExpression expression = TemplateExpressionParser.Parse(input, diagnostics);
            diagnostics.ShouldBeEmpty();
            expression.Accept(TestStringTemplateExpressionVisitor.Instance, new StringBuilder()).ToString().ShouldBe(expectedParsedAst);
            expression.ToString().ShouldBe(input);
        }

        [Theory]
        [InlineData("\\y", "(1,2,1,3): unexpected `y`, expected `\\`, `\'` or `{`")]
        [InlineData("{", "(1,2,1,2): unexpected end of input, expected expression")]
        [InlineData("{'''}", "(1,4,1,5): unexpected `'`, expected `}`")]
        [InlineData("{10-2}", "(1,4,1,5): unexpected `-`, expected `}`")] // - is negation, not subtraction at this point
        [InlineData("{call(", "(1,7,1,7): unexpected end of input, expected `)`")]
        [InlineData("{call()", "(1,8,1,8): unexpected end of input, expected `}`")]
        [InlineData("{call('a' 'b')}", "(1,11,1,12): unexpected `'`, expected `)`")] // this one could be better by suggesting a ,
        public void GivenASyntacticallyInValidExpressionString_WhenParsed_ProducesTheExpectedDiagnostics(string input, string expectedError)
        {
            var diagnostics = new TemplateExpressionDiagnosticCollection();
            TemplateExpressionParser.Parse(input, diagnostics).ShouldBeNull();
            diagnostics.ShouldHaveSingleItem().ToString().ShouldBe(expectedError);
        }

        [Theory]
        [InlineData(@"
{a()}
$$$$$
 ***")]
        [InlineData(@"
/Patient/{fhirpath(search('/patient?organization={fhirPath(search('PractitionerRole?identifier=http://example.com/aad|{claims('sub')}'), 'organization')}', 'id'))}/*
$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$
''''''''' ******************************************************************************************************************************************************** ''
                   **********************************************************************************************************************************************
                          $$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$  $$$$
                           '''''''''''''''''''''' ******************************************************************************************************     ''
                                                           ****************************************************************************  $$$$$$$$$$$$$$
                                                                  $$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$    ''''''''''''
                                                                   ''''''''''''''''''''''''''''''''''''''''''''''''''' *************
                                                                                                                              $$$$$
                                                                                                                               '''")]
        public void GivenASyntacticallyValidExpressionString_WhenParsed_CreatesAnExpressionTreeWithTheExpectedTextSpans(string data)
        {
            string[] lines = data.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var input = lines[0];
            var expected = string.Join(Environment.NewLine, lines.Skip(1));

            var diagnostics = new TemplateExpressionDiagnosticCollection();
            TemplateExpression expression = TemplateExpressionParser.Parse(input, diagnostics);
            Assert.Empty(diagnostics);
            Assert.Equal(expected, PositionVisitor.Visit(expression));
        }
    }
}
