// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Shared.Core.Features.Validation;

namespace Microsoft.Health.Fhir.Shared.Tests.E2E.Rest
{
    /// <summary>
    /// Test provider for $validate tests.
    /// </summary>
    /// <remarks>
    /// Replaces <see cref="IProvideProfilesForValidation"/> in test server to <see cref="ProfileReaderFromZip"/>
    /// </remarks>
    public sealed class StartupForValidateTestProvider : StartupBaseForCustomProviders
    {
        public StartupForValidateTestProvider(IConfiguration configuration)
            : base(configuration)
        {
        }

        public override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            var path = Path.GetDirectoryName(GetType().Assembly.Location);
            var profileReader = new ProfileReaderFromZip(Path.Combine(path, "Profiles", $"{ModelInfoProvider.Version}", $"{ModelInfoProvider.Version}.zip"));
            services.Replace(new ServiceDescriptor(typeof(IProvideProfilesForValidation), profileReader));
        }
    }
}
