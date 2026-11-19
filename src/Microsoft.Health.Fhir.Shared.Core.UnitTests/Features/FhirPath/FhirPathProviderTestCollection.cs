// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.FhirPath
{
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class FhirPathProviderTestCollection
    {
        public const string Name = nameof(FhirPathProviderTestCollection);
    }
}
