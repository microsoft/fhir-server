// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Xunit;
using static Microsoft.Health.Fhir.Tests.Common.Search.SearchValueValidationHelper;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Converters
{
    public class MarkdownToStringSearchValueTypeConverterTests : FhirElementToSearchValueTypeConverterTests<MarkdownToStringSearchValueTypeConverter, Markdown>
    {
        [Fact]
        public void GivenAMarkdownWithNoValue_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            Test(md => md.Value = null);
        }

        [Fact]
        public void GivenAMarkdownWithValue_WhenConverted_ThenAStringSearchValueShouldBeCreated()
        {
            const string markdown = "```code```";

            Test(
                md => md.Value = markdown,
                ValidateString,
                markdown);
        }
    }
}
