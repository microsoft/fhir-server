// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using static Microsoft.Health.Fhir.Tests.Common.Search.SearchValueValidationHelper;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Converters
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class CodeToTokenSearchValueConverterTests : FhirInstanceToSearchValueConverterTests<Code>
    {
        public CodeToTokenSearchValueConverterTests()
            : base()
        {
        }

        protected override async Task<ITypedElementToSearchValueConverter> GetTypeConverterAsync()
        {
            var resolver = new CodeSystemResolver(ModelInfoProvider.Instance);
            await resolver.StartAsync(CancellationToken.None);
            return new CodeToTokenSearchValueConverter(resolver);
        }

        [Fact]
        public async Task GivenACode_WhenConverted_ThenATokenSearchValueShouldBeCreated()
        {
            const string code = "code";

            await Test(
                c => c.Value = code,
                ValidateToken,
                new Token(code: code));
        }
    }
}
