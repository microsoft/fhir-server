// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using HotChocolate.Types;

namespace Microsoft.Health.Fhir.Api.Features.GraphQl.Directives
{
    public class FlattenDirective : DirectiveType
    {
        protected override void Configure(IDirectiveTypeDescriptor descriptor)
        {
            descriptor.Name("flatten");
            descriptor.Location(DirectiveLocation.Field);
            descriptor.Use(next => async context =>
            {
                var currentContext = context.Result;
                await next(context);

                if (context.Result is string s)
                {
                    var arr = new List<string>();
                    arr.Add(s.ToUpperInvariant());
                    context.Result = arr;

                    // context.Result = s.ToUpperInvariant();
                }
            });
        }
    }
}
