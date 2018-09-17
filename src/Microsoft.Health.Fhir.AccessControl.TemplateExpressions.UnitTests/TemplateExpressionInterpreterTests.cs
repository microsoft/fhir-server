// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Ast;
using Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Evaluation;
using Xunit;

namespace Microsoft.Health.Fhir.AccessControl.TemplateExpressions.UnitTests
{
    public class TemplateExpressionInterpreterTests : TemplateExpressionEvaluationTestsBase, IClassFixture<FunctionRepositoryFixture>
    {
        private readonly TemplateExpressionInterpreter<TypedServiceProvider> _interpreter;

        public TemplateExpressionInterpreterTests(FunctionRepositoryFixture fixture)
        {
            _interpreter = new TemplateExpressionInterpreter<TypedServiceProvider>(fixture.FunctionRepository);
        }

        private protected override async Task<string> Evaluate(TemplateExpression expression, TypedServiceProvider serviceProvider)
        {
            return await _interpreter.Evaluate(expression, serviceProvider);
        }
    }
}
