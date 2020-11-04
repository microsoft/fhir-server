// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Routing
{
    public static class OperationRegistry
    {
        private static readonly Dictionary<string, Type> _lookupOperation = new Dictionary<string, Type>();

        public static IServiceCollection RegisterOperationsInAssembly(this IServiceCollection serviceCollection, params Assembly[] assemblies)
        {
            foreach (var operationType in assemblies.SelectMany(x => x.GetTypes()).Where(x => x.GetInterfaces().Any(y => typeof(IOperationRequest).IsAssignableFrom(y))))
            {
                RegisterOperation(serviceCollection, operationType);
            }

            return serviceCollection;
        }

        public static IServiceCollection RegisterOperation(this IServiceCollection serviceCollection, Type operationRequest)
        {
            serviceCollection.Add(operationRequest)
                .Scoped()
                .AsSelf()
                .AsImplementedInterfaces();

            if (!(operationRequest.GetCustomAttribute(typeof(OperationAttribute)) is OperationAttribute name))
            {
                throw new Exception($"{operationRequest.Name} requires an Operation attribute.");
            }

            foreach (var method in name.HttpMethods)
            {
                _lookupOperation.Add($"{name.OperationName.ToUpperInvariant()}_{method.ToUpperInvariant()}", operationRequest);
            }

            return serviceCollection;
        }

        public static Type FindOperation(string name, string httpMethod)
        {
            if (_lookupOperation.TryGetValue($"{name.ToUpperInvariant()}_{httpMethod.ToUpperInvariant()}", out var type))
            {
                return type;
            }

            return null;
        }
    }
}
