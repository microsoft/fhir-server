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
    public class TemplateExpressionRoslynCompilerTests : TemplateExpressionEvaluationTestsBase, IClassFixture<FunctionRepositoryFixture>
    {
        private readonly TemplateExpressionRoslynCompiler<TypedServiceProvider> _compiler;

        public TemplateExpressionRoslynCompilerTests(FunctionRepositoryFixture fixture)
        {
            _compiler = new TemplateExpressionRoslynCompiler<TypedServiceProvider>(fixture.FunctionRepository);
        }

        private protected override Task<string> Evaluate(TemplateExpression expression, TypedServiceProvider serviceProvider)
        {
            return _compiler.Compile(expression)(serviceProvider).AsTask();
        }
    }
}
