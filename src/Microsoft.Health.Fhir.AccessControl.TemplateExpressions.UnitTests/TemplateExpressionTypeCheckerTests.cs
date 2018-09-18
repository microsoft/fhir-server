// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Evaluation;
using Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Parser;
using Shouldly;
using Xunit;

namespace Microsoft.Health.Fhir.AccessControl.TemplateExpressions.UnitTests
{
    public class TemplateExpressionTypeCheckerTests : IClassFixture<FunctionRepositoryFixture>
    {
        private readonly TemplateExpressionTypeChecker _typeChecker;

        public TemplateExpressionTypeCheckerTests(FunctionRepositoryFixture fixture)
        {
            _typeChecker = new TemplateExpressionTypeChecker(fixture.FunctionRepository);
        }

        [Theory]
        [InlineData("{missingMethod()}", "(1,2,1,17): Function 'missingMethod' not found. Options are: 'add', 'claims', 'concat', 'httpRequest', 'parseInt'.")]
        [InlineData("{parseInt()}", "(1,2,1,12): An argument for required parameter 's' was not provided for call to 'parseInt'.")]
        [InlineData("{parseInt('123')}")]
        [InlineData("{parseInt(parseInt('123'))}", "(1,11,1,26): Argument of type 'Int32' is not assignable to parameter 's' of type 'String'.")]
        [InlineData("{add(1, -1)}")]
        [InlineData("{add(1, parseInt('2'))}")]
        [InlineData("{httpRequest('http://foo')}")]
        [InlineData("{httpRequest('http://foo', 'GET')}")]
        [InlineData("{httpRequest('http://foo', 'GET', 'extra')}", "(1,35,1,42): 3 arguments were provided for call to function 'httpRequest', which has only 2 parameters.")]
        [InlineData("{parseInt(httpRequest('http://foo'))}")]
        public void GivenAnExpression_WhenPerformingTypeChecking_TheExpectedDiagnosticsAreProduced(string input, params string[] expectedErrors)
        {
            var diagnostics = new TemplateExpressionDiagnosticCollection();
            var expression = TemplateExpressionParser.Parse(input, diagnostics);
            diagnostics.ShouldBeEmpty();
            expression.Accept(_typeChecker, diagnostics);

            diagnostics.Select(d => d.ToString()).ShouldBe(expectedErrors, ignoreOrder: true);
        }
    }
}
