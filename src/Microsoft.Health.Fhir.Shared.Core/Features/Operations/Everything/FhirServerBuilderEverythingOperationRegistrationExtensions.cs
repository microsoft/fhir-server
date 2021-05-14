// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations.Everything;
using Microsoft.Health.Fhir.Core.Registration;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class FhirServerBuilderEverythingOperationRegistrationExtensions
    {
        public static IFhirServerBuilder AddEverythingOperation(this IFhirServerBuilder fhirServerBuilder)
        {
            EnsureArg.IsNotNull(fhirServerBuilder, nameof(fhirServerBuilder));
            fhirServerBuilder.Services.AddSingleton<IPatientEverythingService, PatientEverythingService>();

            return fhirServerBuilder;
        }
    }
}
