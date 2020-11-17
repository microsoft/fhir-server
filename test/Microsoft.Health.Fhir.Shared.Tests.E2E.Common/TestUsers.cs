// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Tests.E2E.Common
{
    public static class TestUsers
    {
        public static TestUser ReadOnlyUser { get; } = new TestUser("fhirReaderUser");

        public static TestUser ReadWriteUser { get; } = new TestUser("fhirWriterUser");

        public static TestUser ExportUser { get; } = new TestUser("fhirExporterUser");

        public static TestUser AdminUser { get; } = new TestUser("fhirAdminUser");
    }
}
