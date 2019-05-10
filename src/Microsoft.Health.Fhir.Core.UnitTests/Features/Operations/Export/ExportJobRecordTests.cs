// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Export
{
    public class ExportJobRecordTests
    {
        private static readonly Uri RequestUri = new Uri("http://localhost/123");

        [Fact]
        public void GivenAExportJobRecord_WhenCalculatingHash_ThenExportRequestUriShouldBeUsed()
        {
            var exportJobRecord = new ExportJobRecord(RequestUri);

            Assert.Equal("F17B858DCF310C3A9445BB4119D4BA6465E421E87B2C1ED75B55A69F9674FD70", exportJobRecord.Hash);
        }

        [Fact]
        public void GivenAExportJobRecord_WhenCalculatingHash_ThenRequestorClaimsShouldBeUsed()
        {
            var exportJobRecord = new ExportJobRecord(RequestUri);
            var exportJobRecord2 = new ExportJobRecord(RequestUri, new List<KeyValuePair<string, string>>() { KeyValuePair.Create("Claim", "Value") });

            Assert.NotEqual(exportJobRecord.Hash, exportJobRecord2.Hash);
        }
    }
}
