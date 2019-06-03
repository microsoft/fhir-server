// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Tests.E2E.Crucible.Client.Models;

namespace Microsoft.Health.Fhir.Tests.E2E.Crucible
{
    public class ServerTestRun
    {
        public ServerTestRun(string serverBase, TestRun lastRun)
        {
            EnsureArg.IsNotNullOrEmpty(serverBase, nameof(serverBase));
            EnsureArg.IsNotNull(lastRun, nameof(lastRun));

            ServerUrl = serverBase;
            TestRun = lastRun;
        }

        public string ServerUrl { get; set; }

        public TestRun TestRun { get; set; }

        public string GetPermalink(Result result, string parentTestId)
        {
            EnsureArg.IsNotNullOrEmpty(TestRun.Id, nameof(TestRun.Id));
            EnsureArg.IsNotNull(result, nameof(result));

            return $"{ServerUrl}#{TestRun.Id}/{result.TestId ?? parentTestId}/{result.Id}";
        }
    }
}
