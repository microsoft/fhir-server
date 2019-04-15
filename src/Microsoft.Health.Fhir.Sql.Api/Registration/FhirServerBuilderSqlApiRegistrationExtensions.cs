// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class FhirServerBuilderSqlApiRegistrationExtensions
    {
        public static IServiceCollection AddFhirServerSqlApi(this IServiceCollection serviceCollection)
        {
            EnsureArg.IsNotNull(serviceCollection, nameof(serviceCollection));

            return serviceCollection;
        }
    }
}
