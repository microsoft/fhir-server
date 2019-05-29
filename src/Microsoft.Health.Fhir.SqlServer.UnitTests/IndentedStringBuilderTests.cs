// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests
{
    public class IndentedStringBuilderTests
    {
        [Fact]
        public void GivenAnIndentedStringBuilder_WhenUsingIndentedScopes_KeepsTrackOfIndentation()
        {
            var sb = new IndentedStringBuilder(new StringBuilder())
                .AppendLine("class Foo").AppendLine("{");

            using (sb.Indent())
            {
                sb.AppendLine("Foo()").AppendLine("{");
                using (sb.Indent())
                {
                    sb.Append("// ").AppendLine("hello");
                }

                sb.AppendLine("}");
            }

            sb.Append("}");

            Assert.Equal($"class Foo{Environment.NewLine}{{{Environment.NewLine}    Foo(){Environment.NewLine}    {{{Environment.NewLine}        // hello{Environment.NewLine}    }}{Environment.NewLine}}}", sb.ToString());
        }
    }
}
