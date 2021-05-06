// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations.MemberMatch;
using Microsoft.Health.Fhir.Core.Registration;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class FhirServerBuilderMemberMatchRegistrationExtensions
    {
        public static IFhirServerBuilder AddMemberMatch(this IFhirServerBuilder fhirServerBuilder)
        {
            EnsureArg.IsNotNull(fhirServerBuilder, nameof(fhirServerBuilder));
            fhirServerBuilder.Services.AddSingleton<IMemberMatchService, MemberMatchService>();

            return fhirServerBuilder;
        }
    }
}
