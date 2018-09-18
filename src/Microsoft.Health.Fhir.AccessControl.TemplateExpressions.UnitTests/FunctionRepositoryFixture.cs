// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Evaluation;
using NSubstitute;
using Shouldly;

namespace Microsoft.Health.Fhir.AccessControl.TemplateExpressions.UnitTests
{
    public class FunctionRepositoryFixture
    {
        public FunctionRepositoryFixture()
        {
            FunctionRepository = new TemplateExpressionFunctionRepository(
                new[]
                {
                    Function<HttpRequest>((url, method) => Task.FromResult($"{method} {url}")),
                    Function<Claims>((name, d) => $"{name}:{d[name]}"),
                    Function<ParseInt>((culture, input) =>
                    {
                        culture.ShouldNotBeNull();
                        return int.Parse(input, culture);
                    }),
                    Function<Add>((a, b) => a + b),
                    Function<Concat>((s1, s2, s3, s4) => string.Concat(s1, s2, s3, s4)),
                });
        }

        public delegate Task<string> HttpRequest(string url, string method = "GET");

        public delegate string Claims(string claimName, [Injected]Dictionary<string, string> claims);

        public delegate int ParseInt([Injected]CultureInfo culture, string s);

        public delegate int Add(int a, int b);

        public delegate string Concat(object s1 = null, object s2 = null, object s3 = null, object s4 = null);

        internal TemplateExpressionFunctionRepository FunctionRepository { get; set; }

        private ITemplateExpressionFunction Function<TDelegate>(TDelegate @delegate)
            where TDelegate : Delegate
        {
            var function = Substitute.For<ITemplateExpressionFunction>();
            function.Name.Returns(char.ToLowerInvariant(@delegate.GetType().Name[0]) + @delegate.GetType().Name.Substring(1));
            function.Delegate.Returns(@delegate);
            return function;
        }
    }
}
