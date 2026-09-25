// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Schema
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Schema)]
    public class SchemaFoundationTests
    {
        [Fact]
        public void GivenReleasedSchemaGenerator_WhenLatestModelIsBuilt_ThenVectorColumnMetadataIsAvailable()
        {
            VectorColumn embedding = VLatest.VectorSearchParam.Embedding;

            Assert.Equal("Embedding", embedding.ToString());
            Assert.Equal(1536, embedding.Dimensions);
            Assert.Equal("SourceTextCompressed", VLatest.VectorSearchParam.SourceTextCompressed.ToString());
        }

        [Fact]
        public void GivenVectorMigrationScript_WhenRead_ThenUnsupportedEnginesAreRejectedBeforeVectorDdl()
        {
            var scriptProvider = new ScriptProvider<SchemaVersion>();

            string script = scriptProvider.GetMigrationScript((int)SchemaVersion.V118, applyFullSchemaSnapshot: false);

            int guardIndex = script.IndexOf("sys.types", StringComparison.Ordinal);
            int vectorTableIndex = script.IndexOf("VectorSearchParam", StringComparison.Ordinal);
            Assert.True(guardIndex >= 0, "The migration script must validate native vector support.");
            Assert.True(vectorTableIndex > guardIndex, "The native vector support guard must execute before vector DDL.");
        }

        [Fact]
        public void GivenFullSchemaSnapshot_WhenRead_ThenVectorObjectsAreCreatedWithoutAnEngineGuard()
        {
            var scriptProvider = new ScriptProvider<SchemaVersion>();

            string script = scriptProvider.GetMigrationScript((int)SchemaVersion.V118, applyFullSchemaSnapshot: true);

            // A fresh install is guarded by the native vector DDL itself: CREATE TABLE fails inside the
            // initialization transaction when the engine has no vector type. The shared initialization
            // script deliberately carries no feature-specific engine check.
            Assert.Contains("VectorSearchParam", script, StringComparison.Ordinal);
            Assert.DoesNotContain("50419", script, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenConsolidatedVectorSchema_WhenVersionConstantsAreRead_ThenOnlyVersion118IsRequired()
        {
            Assert.Equal((int)SchemaVersion.V118, SchemaVersionConstants.Max);
            Assert.Equal((int)SchemaVersion.V118, SchemaVersionConstants.VectorSearchVersion);
        }
    }
}
