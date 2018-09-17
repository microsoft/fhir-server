// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Ast;
using Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Evaluation;
using Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Parser;
using Shouldly;
using Superpower.Model;
using Xunit;

namespace Microsoft.Health.Fhir.AccessControl.TemplateExpressions.UnitTests
{
    public abstract class TemplateExpressionEvaluationTestsBase
    {
        private protected abstract Task<string> Evaluate(TemplateExpression expression, TypedServiceProvider serviceProvider);

        [Theory]
        [InlineData("{httpRequest('https://foo/{claims('sub')}', 'POST')}", "POST https://foo/sub:abc")]
        [InlineData("{httpRequest('https://foo/{claims('sub')}')}", "GET https://foo/sub:abc")]
        [InlineData("a{'b'}c", "abc")]
        [InlineData("\"", "\"")]
        [InlineData("\\\\", "\\")]
        [InlineData("{add(add(1, 23), -2)}", "22")]
        [InlineData("1+{add(add(1, 23), -2)}+2", "1+22+2")]
        [InlineData("{concat('1', parseInt('2'), httpRequest('3'))}", "12GET 3")]
        [InlineData("{httpRequest(httpRequest('4'), httpRequest('POST'))}", "GET POST GET 4")]
        [InlineData("{concat(httpRequest('1'), httpRequest('3'))}", "GET 1GET 3")]
        [InlineData("1{parseInt('2')}{httpRequest('3')}{httpRequest('4')}{httpRequest('5')}", "12GET 3GET 4GET 5")]
        public async Task GivenAValidExpression_WhenEvaluated_ReturnsTheExpectedResult(string input, object expectedValue)
        {
            var diagnostics = new TemplateExpressionDiagnosticCollection();
            var expression = TemplateExpressionParser.Parse(input, diagnostics);
            diagnostics.ShouldBeEmpty();
            expression = expression.Accept(ConstantFolder.Instance, Unit.Value);
            object result = await Evaluate(expression, new TypedServiceProvider());
            result.ShouldBe(expectedValue);
        }

        public class TypedServiceProvider : IServiceProvider
        {
            public Dictionary<string, string> Claims { get; } = new Dictionary<string, string> { { "sub", "abc" } };

            public object GetService(Type serviceType)
            {
                if (serviceType == typeof(CultureInfo))
                {
                    return CultureInfo.InvariantCulture;
                }

                serviceType.ShouldNotBe(typeof(Dictionary<string, string>), $"typed property {nameof(Claims)} should be used instead");

                return null;
            }
        }
    }
}
