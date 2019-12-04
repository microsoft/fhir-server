// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Features.Bundle;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class BundleAwareJwtBearerExtensions
    {
        public static AuthenticationBuilder AddBundleAwareCustomJwtBearer(this AuthenticationBuilder builder)
            => builder.AddBundleAwareCustomJwtBearer(JwtBearerDefaults.AuthenticationScheme, _ => { });

        public static AuthenticationBuilder AddBundleAwareCustomJwtBearer(this AuthenticationBuilder builder, Action<JwtBearerOptions> configureOptions)
            => builder.AddBundleAwareCustomJwtBearer(JwtBearerDefaults.AuthenticationScheme, configureOptions);

        public static AuthenticationBuilder AddBundleAwareCustomJwtBearer(this AuthenticationBuilder builder, string authenticationScheme, Action<JwtBearerOptions> configureOptions)
            => builder.AddBundleAwareCustomJwtBearer(authenticationScheme, displayName: null, configureOptions: configureOptions);

        public static AuthenticationBuilder AddBundleAwareCustomJwtBearer(this AuthenticationBuilder builder, string authenticationScheme, string displayName, Action<JwtBearerOptions> configureOptions)
        {
            builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<JwtBearerOptions>, JwtBearerPostConfigureOptions>());
            return builder.AddScheme<JwtBearerOptions, BundleAwareJwtBearerHandler>(authenticationScheme, displayName, configureOptions);
        }
    }
}
