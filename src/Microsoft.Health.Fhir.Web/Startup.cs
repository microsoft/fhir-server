// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.Web
{
    public class Startup : Microsoft.Health.Fhir.Api.StartupBase<Startup>
    {
        public Startup(IHostingEnvironment env, ILogger<Startup> logger, IConfiguration configuration)
            : base(env, logger, configuration)
        {
        }

        protected override IEnumerable<Assembly> AssembliesContainingStartupModules
        {
            get { return base.AssembliesContainingStartupModules.Concat(new[] { typeof(Startup).Assembly }); }
        }
    }
}
