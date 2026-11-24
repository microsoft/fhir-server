// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class CustomQueryProceduresTests
    {
        [Fact]
        public void GivenProcedureNames_WhenCreated_ThenOnlyCustomQueryProceduresAreKeyedByHash()
        {
            var procedures = CustomQueryProcedures.Create(["CustomQuery_hash1", "badData", "CustomQuery_hash1", "CustomQuery_a_b", "OtherQuery_hash3", "CustomQuery_hash2"]);

            Assert.True(procedures.TryGetProcedureName("hash1", out string first));
            Assert.Equal("CustomQuery_hash1", first);
            Assert.True(procedures.TryGetProcedureName("hash2", out string second));
            Assert.Equal("CustomQuery_hash2", second);
            Assert.False(procedures.TryGetProcedureName("hash3", out _));
            Assert.False(procedures.TryGetProcedureName("a", out _));
        }

        [Fact]
        public void GivenAProcedureWithALowercasePrefix_WhenCreated_ThenItIsStillUsed()
        {
            var procedures = CustomQueryProcedures.Create(["customquery_Hash1"]);

            Assert.True(procedures.TryGetProcedureName("Hash1", out string name));
            Assert.Equal("customquery_Hash1", name);
        }
    }
}
