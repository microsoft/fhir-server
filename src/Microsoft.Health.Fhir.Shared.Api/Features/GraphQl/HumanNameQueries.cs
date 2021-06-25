// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Hl7.Fhir.Model;
using HotChocolate.Types;
using MediatR;

namespace Microsoft.Health.Fhir.Api.Features.GraphQl
{
    [ExtendObjectType(Name = "Query")]
#pragma warning disable CA1041 // Provide ObsoleteAttribute message
    [System.Obsolete]
#pragma warning restore CA1041 // Provide ObsoleteAttribute message
    public class HumanNameQueries
    {
        private IMediator _mediator;

        public HumanNameQueries(IMediator mediator)
        {
            _mediator = mediator;
        }

        public IQueryable<HumanName> GetFoos(IMediator mediator) => (IQueryable<HumanName>)mediator.GetType();
    }
}
