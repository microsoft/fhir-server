// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Legacy.SearchValues
{
    public static class ExtractorTestHelper
    {
        public static void ValidateCollectionIsEmpty(IReadOnlyCollection<ISearchValue> collection)
        {
            Assert.NotNull(collection);
            Assert.Empty(collection);
        }

        public static void ValidateComposite<TSearchValue>(
            string expectedSystem,
            string expectedCode,
            ISearchValue actualValue,
            Action<TSearchValue> valueValidator)
            where TSearchValue : ISearchValue
        {
            Assert.NotNull(actualValue);

            LegacyCompositeSearchValue csv = Assert.IsType<LegacyCompositeSearchValue>(actualValue);

            Assert.Equal(expectedSystem, csv.System);
            Assert.Equal(expectedCode, csv.Code);
            Assert.NotNull(csv.Value);

            TSearchValue dtsv = Assert.IsType<TSearchValue>(csv.Value);

            valueValidator(dtsv);
        }
    }
}
