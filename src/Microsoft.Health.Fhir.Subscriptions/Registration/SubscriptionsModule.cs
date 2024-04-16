// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Subscriptions.Registration
{
    public class SubscriptionsModule : IStartupModule
    {
        public void Load(IServiceCollection services)
        {
            services.TypesInSameAssemblyAs<SubscriptionsModule>()
                .AssignableTo<IJob>()
                .Transient()
                .AsSelf()
                .AsService<IJob>();
        }
    }
}
