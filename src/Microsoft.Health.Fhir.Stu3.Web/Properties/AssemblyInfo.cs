// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
[assembly: InternalsVisibleTo("Microsoft.Health.Fhir.Stu3.Web.UnitTests")]

// The Stu3 web unit test project builds as Microsoft.Health.Fhir.Web.UnitTests (see its AssemblyName).
[assembly: InternalsVisibleTo("Microsoft.Health.Fhir.Web.UnitTests")]
