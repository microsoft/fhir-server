// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Api.Configs
{
    public class FhirServerConfiguration
    {
        public FeatureConfiguration Features { get; } = new FeatureConfiguration();

        public ConformanceConfiguration Conformance { get; } = new ConformanceConfiguration();

        public SecurityConfiguration Security { get; } = new SecurityConfiguration();

        public virtual CorsConfiguration Cors { get; } = new CorsConfiguration();

        public OperationsConfiguration Operations { get; } = new OperationsConfiguration();

        public AuditConfiguration Audit { get; } = new AuditConfiguration();
    }
}
