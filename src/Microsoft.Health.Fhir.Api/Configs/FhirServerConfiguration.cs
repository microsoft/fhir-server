// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Api.Configuration;
using Microsoft.Health.Api.Features.Cors;
using Microsoft.Health.Core.Configs;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features;

namespace Microsoft.Health.Fhir.Api.Configs
{
    public class FhirServerConfiguration : IApiConfiguration
    {
        public string PathBase { get; set; } = string.Empty;

        public FeatureConfiguration Features { get; } = new FeatureConfiguration();

        public SecurityConfiguration Security { get; } = new SecurityConfiguration();

        public virtual CorsConfiguration Cors { get; } = new CorsConfiguration();

        public OperationsConfiguration Operations { get; } = new OperationsConfiguration();

        public AuditConfiguration Audit { get; } = new AuditConfiguration(KnownHeaders.CustomAuditHeaderPrefix);

        public CoreFeatureConfiguration CoreFeatures { get; } = new CoreFeatureConfiguration();

        public BundleConfiguration Bundle { get; } = new BundleConfiguration();

        public ThrottlingConfiguration Throttling { get; } = new ThrottlingConfiguration();

        public ArtifactStoreConfiguration ArtifactStore { get; } = new ArtifactStoreConfiguration();

        public ImplementationGuidesConfiguration ImplementationGuides { get; } = new ImplementationGuidesConfiguration();

        public EncryptionConfiguration Encryption { get; } = new EncryptionConfiguration();
    }
}
