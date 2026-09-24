// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;

// This tool is a developer utility; console output does not need to be localized.
[assembly: SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters", Justification = "Developer tool console output.", Scope = "module")]
[assembly: SuppressMessage("Performance", "CA1849:Call async methods when in an async method", Justification = "Console and small-file IO in a developer tool.", Scope = "module")]
[assembly: SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Developer tool executes operator-supplied scripts by design.", Scope = "module")]
