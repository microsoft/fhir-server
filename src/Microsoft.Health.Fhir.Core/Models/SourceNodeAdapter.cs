// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Specification;
using Hl7.Fhir.Utility;

namespace Microsoft.Health.Fhir.Core.Models
{
    internal class SourceNodeAdapter : ITypedElement, IAnnotated, IExceptionSource
    {
        private readonly ISourceNode _current;

        public SourceNodeAdapter(ISourceNode node)
        {
            EnsureArg.IsNotNull(node, nameof(node));

            _current = node;

            if (!(node is IExceptionSource exceptionSource) || exceptionSource.ExceptionHandler != null)
            {
                return;
            }

            exceptionSource.ExceptionHandler = (source, notification) => ExceptionHandler.NotifyOrThrow(source, notification);
        }

        private SourceNodeAdapter(ISourceNode sourceNode, ExceptionNotificationHandler exceptionHandler)
        {
            EnsureArg.IsNotNull(sourceNode, nameof(sourceNode));

            _current = sourceNode;
            ExceptionHandler = exceptionHandler;
        }

        public ExceptionNotificationHandler ExceptionHandler { get; set; }

        public ISourceNode SourceNode => _current;

        public string Name
        {
            get => _current.Name;
        }

        public string InstanceType
        {
            get => _current.GetResourceTypeIndicator();
        }

        public object Value
        {
            get => _current.Text;
        }

        public string Location
        {
            get => _current.Location;
        }

        public IElementDefinitionSummary Definition
        {
            get => null;
        }

        public IEnumerable<ITypedElement> Children(string name) => _current.Children(name).Select(c => new SourceNodeAdapter(c, ExceptionHandler));

        IEnumerable<object> IAnnotated.Annotations(Type type) => _current.Annotations(type);
    }
}
