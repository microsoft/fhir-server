// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Evaluation;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.AccessControl.TemplateExpressions.UnitTests
{
    public class FunctionMetadataTests
    {
        [Fact]
        public void GivenAnITemplateExpressionFunctionWithAnEmptyName_WhenContructingAFunctionMetadataInstance_ThenAnArgumentExceptionIsThrown()
        {
            var function = Substitute.For<ITemplateExpressionFunction>();
            function.Name.Returns(" ");
            function.Delegate.Returns(new Func<IServiceProvider, object>(sp => null));
            Assert.Throws<ArgumentException>(() => new FunctionMetadata(function));
        }

        [Fact]
        public void GivenAnITemplateExpressionFunctionWithANullDelegate_WhenContructingAFunctionMetadataInstance_ThenAnArgumentExceptionIsThrown()
        {
            var function = Substitute.For<ITemplateExpressionFunction>();
            function.Name.Returns("f");
            Assert.Throws<ArgumentException>(() => new FunctionMetadata(function));
        }

        [Fact]
        public void GivenAnITemplateExpressionFunctionWithADelegateWithVoidReturnType_WhenContructingAFunctionMetadataInstance_ThenAnArgumentExceptionIsThrown()
        {
            var function = Substitute.For<ITemplateExpressionFunction>();
            function.Name.Returns("f");
            function.Delegate.Returns(new Action<IServiceProvider>(sp => { }));
            Assert.Throws<ArgumentException>(() => new FunctionMetadata(function));
        }
    }
}
