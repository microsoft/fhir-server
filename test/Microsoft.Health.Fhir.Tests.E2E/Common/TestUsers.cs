// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Tests.E2E.Common
{
    public static class TestUsers
    {
        public static TestUser ReadOnlyUser { get; } = new TestUser("john");

        public static TestUser WriteOnlyUser { get; } = new TestUser("sam");

        public static TestUser ReadWriteUser { get; } = new TestUser("frank");

        public static TestUser HardDeleteUser { get; } = new TestUser("doug");

        public static TestUser ExportUser { get; } = new TestUser("steve");

        public static TestUser AdminUser { get; } = new TestUser("itguy");
    }
}
