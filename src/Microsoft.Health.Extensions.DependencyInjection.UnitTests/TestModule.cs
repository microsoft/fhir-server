// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Health.Extensions.DependencyInjection.UnitTests
{
    public class TestModule : IStartupModule
    {
        public void Load(IServiceCollection services)
        {
            services.AddScoped<TestComponent>();
        }
    }
}
