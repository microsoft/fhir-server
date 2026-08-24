// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// What a script reported.
    /// </summary>
    /// <param name="ExitCode">The exit code the leg reads to decide whether to fail.</param>
    /// <param name="Output">Everything the script wrote, on either stream.</param>
    internal sealed record ScriptRun(int ExitCode, string Output);
}
