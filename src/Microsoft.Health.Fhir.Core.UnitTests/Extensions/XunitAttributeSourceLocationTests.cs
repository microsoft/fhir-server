// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Extensions.Xunit;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Extensions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public sealed class XunitAttributeSourceLocationTests
    {
        [Fact]
        public void GivenCustomFactAttributes_WhenConstructedWithSourceLocation_ThenBaseSourceLocationIsPopulated()
        {
            const string sourceFilePath = "test-file.cs";
            const int sourceLineNumber = 123;

            var retryFact = new RetryFactAttribute(sourceFilePath, sourceLineNumber);
            var skippableFact = new SkippableFactAttribute(sourceFilePath, sourceLineNumber);

            Assert.Equal(sourceFilePath, retryFact.SourceFilePath);
            Assert.Equal(sourceLineNumber, retryFact.SourceLineNumber);
            Assert.Equal(sourceFilePath, skippableFact.SourceFilePath);
            Assert.Equal(sourceLineNumber, skippableFact.SourceLineNumber);
        }

        [Fact]
        public void GivenCustomTheoryAttributes_WhenConstructedWithSourceLocation_ThenBaseSourceLocationIsPopulated()
        {
            const string sourceFilePath = "test-file.cs";
            const int sourceLineNumber = 123;

            var retryTheory = new RetryTheoryAttribute(sourceFilePath, sourceLineNumber);
            var skippableTheory = new SkippableTheoryAttribute(sourceFilePath, sourceLineNumber);

            Assert.Equal(sourceFilePath, retryTheory.SourceFilePath);
            Assert.Equal(sourceLineNumber, retryTheory.SourceLineNumber);
            Assert.Equal(sourceFilePath, skippableTheory.SourceFilePath);
            Assert.Equal(sourceLineNumber, skippableTheory.SourceLineNumber);
        }
    }
}
