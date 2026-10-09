// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Resources;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Microsoft.Health.Fhir.SqlServer.UnitTests")]

// Castle DynamicProxy backs NSubstitute; without this it cannot produce a substitute value for a generic type
// closed over an internal type, which is what mocking ISqlRetryService reads in the watchdogs requires.
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
[assembly: InternalsVisibleTo("Microsoft.Health.Fhir.Stu3.Tests.Integration")]
[assembly: InternalsVisibleTo("Microsoft.Health.Fhir.R4.Tests.Integration")]
[assembly: InternalsVisibleTo("Microsoft.Health.Fhir.R4B.Tests.Integration")]
[assembly: InternalsVisibleTo("Microsoft.Health.Fhir.R5.Tests.Integration")]
[assembly: InternalsVisibleTo("Microsoft.Health.Fhir.Stu3.Tests.E2E")]
[assembly: InternalsVisibleTo("Microsoft.Health.Fhir.R4.Tests.E2E")]
[assembly: InternalsVisibleTo("Microsoft.Health.Fhir.R4B.Tests.E2E")]
[assembly: InternalsVisibleTo("Microsoft.Health.Fhir.R5.Tests.E2E")]
[assembly: InternalsVisibleTo("Microsoft.Health.Internal.Fhir.EventsReader")]
[assembly: InternalsVisibleTo("Microsoft.Health.Internal.Fhir.PerfTester")]
[assembly: InternalsVisibleTo("Microsoft.Health.Internal.Fhir.Exporter")]
[assembly: NeutralResourcesLanguage("en-us")]
