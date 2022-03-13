// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1413:Use trailing comma in multi-line initializers", Justification = "Readability", Scope = "member", Target = "~M:Microsoft.Health.Fhir.Store.Copy.Program.RunOsCommand(System.String,System.String,System.Boolean)")]
[assembly: SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Not used for security", Scope = "member", Target = "~M:Microsoft.Health.Fhir.Store.Copy.Program.CopyViaBcp(System.Byte,System.Int16,System.Int32,System.Int64,System.Int64)")]
