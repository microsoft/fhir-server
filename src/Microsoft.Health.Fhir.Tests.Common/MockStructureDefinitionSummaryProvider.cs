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

namespace Microsoft.Health.Fhir.Tests.Common
{
    /// <summary>
    /// Provides an implementation of IStructureDefinitionSummaryProvider that can be used to execute FHIRPath without the need for version specific references.
    /// </summary>
    public class MockStructureDefinitionSummaryProvider : IStructureDefinitionSummaryProvider
    {
        private readonly ISourceNode _node;

        public MockStructureDefinitionSummaryProvider(ISourceNode node, HashSet<string> seenTypes)
        {
            EnsureArg.IsNotNull(node, nameof(node));
            EnsureArg.IsNotNull(seenTypes, nameof(seenTypes));

            SeenTypes = seenTypes;
            _node = node;
        }

        public HashSet<string> SeenTypes { get; }

        public IStructureDefinitionSummary Provide(string canonical)
        {
            return new MockElementDefinitionSummary(SeenTypes, _node);
        }

        private class MockElementDefinitionSummary : IElementDefinitionSummary, IStructureDefinitionSummary, IAnnotated, IResourceTypeSupplier
        {
            private readonly ISourceNode[] _items;
            private Dictionary<string, IElementDefinitionSummary> _list;
            private readonly string _resourceTypeIndicator;

            public MockElementDefinitionSummary(HashSet<string> seenTypes, ISourceNode item)
                : this(seenTypes, new[] { item }, false)
            {
            }

            public MockElementDefinitionSummary(HashSet<string> seenTypes, ISourceNode[] items, bool isCollection)
            {
                EnsureArg.IsNotNull(seenTypes, nameof(seenTypes));
                EnsureArg.IsNotNull(items, nameof(items));

                SeenTypes = seenTypes;
                IsCollection = isCollection;
                _items = items;

                _resourceTypeIndicator = items.First().GetResourceTypeIndicator();
                if (!string.IsNullOrEmpty(_resourceTypeIndicator))
                {
                    SeenTypes.Add(_resourceTypeIndicator);
                }
            }

            public string ElementName => _items.First().Name;

            public HashSet<string> SeenTypes { get; }

            public bool IsCollection { get; }

            public bool IsRequired { get; }

            public bool InSummary { get; }

            public bool IsChoiceElement { get; }

            public string TypeName => _resourceTypeIndicator;

            public bool IsAbstract { get; }

            public bool IsResource => SeenTypes.Contains(TypeName);

            public string DefaultTypeName => TypeName;

            public string ResourceType => TypeName;

            public ITypeSerializationInfo[] Type => new ITypeSerializationInfo[] { this };

            public string NonDefaultNamespace { get; }

            public XmlRepresentation Representation { get; }

            public int Order { get; }

            public bool IsModifier { get; }

            public IReadOnlyCollection<IElementDefinitionSummary> GetElements()
            {
                if (_list == null)
                {
                    _list = new Dictionary<string, IElementDefinitionSummary>();

                    foreach (var item in _items.SelectMany(x => x.Children()).GroupBy(x => x.Name))
                    {
                        if (!_list.ContainsKey(item.Key))
                        {
                            _list.Add(item.Key, new MockElementDefinitionSummary(SeenTypes, item.ToArray(), item.Count() > _items.Length));
                        }
                    }
                }

                return _list.Values;
            }

            public IEnumerable<object> Annotations(Type type)
            {
                return new[] { this };
            }
        }
    }
}
