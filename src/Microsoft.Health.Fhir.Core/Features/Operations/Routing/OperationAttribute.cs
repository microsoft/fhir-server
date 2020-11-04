// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Security;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Routing
{
    [AttributeUsage(AttributeTargets.Class)]
    public class OperationAttribute : Attribute
    {
        public OperationAttribute(string operationName, bool allowAnonymous, string[] httpMethods, DataActions dataActions = DataActions.None)
        {
            EnsureArg.IsNotNullOrEmpty(operationName, nameof(operationName));
            EnsureArg.IsNotNull(httpMethods, nameof(httpMethods));

            OperationName = operationName;
            AllowAnonymous = allowAnonymous;
            HttpMethods = httpMethods;
            DataActions = dataActions;

            if (!allowAnonymous && dataActions == DataActions.None)
            {
                throw new Exception("A DataAction must be specified when authentication is required.");
            }
        }

        public string OperationName { get; }

        public bool AllowAnonymous { get; }

        public IReadOnlyList<string> HttpMethods { get; }

        public DataActions DataActions { get; }
    }
}
