// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Tests.E2E.Common
{
    /*
     * When adding a new user they must be added in the following locations:
     * - /build/jobs/run-tests.yml DotNetCoreCLI@2 Tasks
     * - /testauthenvironment.json
     */

    public static class TestUsers
    {
        public static TestUser ReadOnlyUser { get; } = new TestUser("globalReaderUser");

        public static TestUser ReadWriteUser { get; } = new TestUser("globalWriterUser");

        public static TestUser ExportUser { get; } = new TestUser("globalExporterUser");

        public static TestUser ConvertDataUser { get; } = new TestUser("globalConverterUser");

        public static TestUser BulkImportUser { get; } = new TestUser("globalImporterUser");

        public static TestUser AdminUser { get; } = new TestUser("globalAdminUser");
    }
}
