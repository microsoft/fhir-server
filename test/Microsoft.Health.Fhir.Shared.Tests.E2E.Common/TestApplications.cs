// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Tests.E2E.Common
{
    /*
     * When adding a new service principals they must be added in the following locations:
     * - /build/jobs/e2e-tests.yml
     * - /build/jobs/run-export-tests.yml
     * - /testauthenvironment.json
     */

    public static class TestApplications
    {
        public static TestApplication AdminUser { get; } = new TestApplication("globalAdminUserApp");

        public static TestApplication BulkImportUser { get; } = new TestApplication("globalImporterUserApp");

        public static TestApplication ConvertDataUser { get; } = new TestApplication("globalConverterUserApp");

        public static TestApplication ExportUser { get; } = new TestApplication("globalExporterUserApp");

        public static TestApplication GlobalAdminServicePrincipal { get; } = new TestApplication("globalAdminServicePrincipal");

        public static TestApplication InvalidClient { get; } = new TestApplication("invalidclient");

        public static TestApplication NativeClient { get; } = new TestApplication("nativeClient");

        public static TestApplication ReadOnlyUser { get; } = new TestApplication("globalReaderUserApp");

        public static TestApplication ReadWriteUser { get; } = new TestApplication("globalWriterUserApp");

        public static TestApplication SmartUserClient { get; } = new TestApplication("smartUserClient");

        public static TestApplication WrongAudienceClient { get; } = new TestApplication("wrongAudienceClient");
    }
}
