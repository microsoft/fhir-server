// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// Helper that determines whether convert-data tests should run given the current test environment.
    /// </summary>
    internal static class ConvertDataTestMode
    {
        /// <summary>
        /// Returns <see langword="true"/> when convert-data tests should execute.
        /// Remote (deployed) servers always expose the feature, so tests run unconditionally.
        /// In-process servers only run when the feature is explicitly enabled in configuration.
        /// </summary>
        /// <param name="isUsingInProcTestServer"><see langword="true"/> when the test is running against an in-process test server.</param>
        /// <param name="configuredEnabled">The <c>Enabled</c> flag from <see cref="Microsoft.Health.Fhir.Core.Configs.ConvertDataConfiguration"/>.</param>
        public static bool IsEnabled(bool isUsingInProcTestServer, bool configuredEnabled)
            => !isUsingInProcTestServer || configuredEnabled;
    }
}
