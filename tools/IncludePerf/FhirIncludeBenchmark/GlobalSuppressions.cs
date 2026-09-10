// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;

// This tool is a developer utility; console output does not need to be localized.
[assembly: SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters", Justification = "Developer tool console output.", Scope = "module")]

// Console and small-file IO on the tool's startup/teardown path is not latency sensitive; the measured
// request path is fully async.
[assembly: SuppressMessage("Performance", "CA1849:Call async methods when in an async method", Justification = "Console and small-file IO outside the measured path.", Scope = "module")]
