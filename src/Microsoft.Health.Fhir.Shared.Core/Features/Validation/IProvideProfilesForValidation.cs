// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Specification.Source;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public interface IProvideProfilesForValidation : IResourceResolver, IKnowSupportedProfiles
    {
        IReadOnlySet<string> GetProfilesTypes();
    }
}
