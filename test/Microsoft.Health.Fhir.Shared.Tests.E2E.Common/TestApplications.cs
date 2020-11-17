﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Tests.E2E.Common
{
    public static class TestApplications
    {
        public static TestApplication FhirAdminServicePrincipal { get; } = new TestApplication("fhirAdminServicePrincipal");

        public static TestApplication NativeClient { get; } = new TestApplication("nativeClient");

        public static TestApplication InvalidClient { get; } = new TestApplication("invalidclient");

        public static TestApplication WrongAudienceClient { get; } = new TestApplication("wrongAudienceClient");
    }
}
