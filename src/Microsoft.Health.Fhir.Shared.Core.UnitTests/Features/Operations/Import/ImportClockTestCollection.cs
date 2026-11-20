// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Operations.Import
{
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class ImportClockTestCollection
    {
        public const string Name = nameof(ImportClockTestCollection);
    }
}
