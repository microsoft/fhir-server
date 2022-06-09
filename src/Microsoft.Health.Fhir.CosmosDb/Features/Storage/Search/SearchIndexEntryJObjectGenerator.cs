// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
////using System.Globalization;
using EnsureThat;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.CosmosDb.Features.Search;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Search
{
    internal class SearchIndexEntryJObjectGenerator : ISearchValueVisitor
    {
        private readonly List<JObject> _generatedObjects = new();

        private static ConcurrentDictionary<string, int> _stringToInt = new ConcurrentDictionary<string, int>();

        private SearchIndexEntry Entry { get; set; }

        private int? Index { get; set; }

        private bool IsCompositeComponent => Index != null;

        private JObject CurrentEntry { get; set; }

        public IReadOnlyList<JObject> Generate(SearchIndexEntry entry)
        {
            EnsureArg.IsNotNull(entry, nameof(entry));

            Entry = entry;
            CurrentEntry = null;
            _generatedObjects.Clear();

            entry.Value.AcceptVisitor(this);

            return _generatedObjects;
        }

        void ISearchValueVisitor.Visit(CompositeSearchValue composite)
        {
            foreach (IEnumerable<ISearchValue> componentValues in composite.Components.CartesianProduct())
            {
                int index = 0;

                CreateEntry();

                try
                {
                    foreach (ISearchValue componentValue in componentValues)
                    {
                        // Set the component index and process individual component of the composite value.
                        Index = index++;

                        componentValue.AcceptVisitor(this);
                    }
                }
                finally
                {
                    Index = null;
                }
            }
        }

        void ISearchValueVisitor.Visit(DateTimeSearchValue dateTime)
        {
            // By default, Json.NET will serialize date time object using format
            // "yyyy'-'MM'-'dd'T'HH':'mm':'ss.FFFFFFFK". The 'F' format specifier
            // will not display if the value is 0. For example, 2018-01-01T00:00:00.0000000+00:00
            // is formatted as 2018-01-01T00:00:00+00:00 but 2018-01-01T00:00:00.9999999+00:00 is
            // formatted as 2018-01-01T00:00:00.9999999+00:00. Because Cosmos DB only supports range index
            // with string or number data type, the comparison does not work correctly in some cases.
            // Output the date time using 'o' to make sure the fraction is always generated.
            AddProperty(SearchValueConstants.DateTimeStartName, dateTime.Start.ToString("yyyyMMddHHmmss"));
            if (dateTime.End.Year < 9999)
            {
                AddProperty(SearchValueConstants.DateTimeEndName, dateTime.Start == dateTime.End ? string.Empty : dateTime.End.ToString("yyyyMMddHHmmss"));
            }
        }

        void ISearchValueVisitor.Visit(NumberSearchValue number)
        {
            if (number.Low == number.High)
            {
                AddProperty(SearchValueConstants.NumberName, number.Low);
            }
            else
            {
                AddProperty(SearchValueConstants.LowNumberName, number.Low);
                AddProperty(SearchValueConstants.HighNumberName, number.High);
            }
        }

        void ISearchValueVisitor.Visit(QuantitySearchValue quantity)
        {
            ////AddPropertyIfNotNull(SearchValueConstants.SystemName, quantity.System);
            AddPropertyIfNotNullWithIntMap(SearchValueConstants.SystemName, quantity.System);
            AddPropertyIfNotNull(SearchValueConstants.CodeName, quantity.Code);

            if (quantity.Low == quantity.High)
            {
                AddProperty(SearchValueConstants.QuantityName, quantity.Low);
            }
            else
            {
                AddProperty(SearchValueConstants.LowQuantityName, quantity.Low);
                AddProperty(SearchValueConstants.HighQuantityName, quantity.High);
            }
        }

        void ISearchValueVisitor.Visit(ReferenceSearchValue reference)
        {
            AddPropertyIfNotNull(SearchValueConstants.ReferenceBaseUriName, reference.BaseUri?.ToString());
            ////AddPropertyIfNotNull(SearchValueConstants.ReferenceResourceTypeName, reference.ResourceType?.ToString());
            if (reference.ResourceType != null)
            {
                AddPropertyIfNotNull(SearchValueConstants.ReferenceResourceTypeName, ResourceKey.NameToId(reference.ResourceType));
            }

            AddProperty(SearchValueConstants.ReferenceResourceIdName, reference.ResourceId);
        }

        void ISearchValueVisitor.Visit(StringSearchValue s)
        {
            if (!IsCompositeComponent)
            {
                ////AddProperty(SearchValueConstants.StringName, s.String);
                AddPropertyWithIntMap(SearchValueConstants.StringName, s.String);
            }

            ////AddProperty(SearchValueConstants.NormalizedStringName, s.String.ToUpperInvariant());
            AddPropertyWithIntMap(SearchValueConstants.NormalizedStringName, s.String.ToUpperInvariant());
        }

        void ISearchValueVisitor.Visit(TokenSearchValue token)
        {
            ////AddPropertyIfNotNull(SearchValueConstants.SystemName, token.System);
            AddPropertyIfNotNullWithIntMap(SearchValueConstants.SystemName, token.System);
            AddPropertyIfNotNull(SearchValueConstants.CodeName, token.Code);

            if (!IsCompositeComponent)
            {
                // Since text is case-insensitive search, it will always be normalized.
                ////AddPropertyIfNotNull(SearchValueConstants.NormalizedTextName, token.Text?.ToUpperInvariant());
                AddPropertyIfNotNullWithIntMap(SearchValueConstants.NormalizedTextName, token.Text?.ToUpperInvariant());
            }
        }

        void ISearchValueVisitor.Visit(UriSearchValue uri)
        {
            AddProperty(SearchValueConstants.UriName, uri.Uri);
            ////if (!SortValueJObjectGenerator.UriToInt.TryGetValue(uri.Uri, out var key))
            ////{
            ////    lock (SortValueJObjectGenerator.UriToInt)
            ////    {
            ////        key = SortValueJObjectGenerator.UriToInt.IsEmpty ? 1 : SortValueJObjectGenerator.UriToInt.Values.Max() + 1;
            ////        AddProperty(SearchValueConstants.UriName, key.ToString());
            ////    }
            ////}
            ////else
            ////{
            ////    AddProperty(SearchValueConstants.UriName, key.ToString());
            ////}
        }

        private void CreateEntry()
        {
            CurrentEntry = new JObject
            {
                new JProperty(SearchValueConstants.ParamName, Entry.SearchParameter.Code),
            };

            _generatedObjects.Add(CurrentEntry);
        }

        private void AddProperty(string name, object value)
        {
            if (Index != null)
            {
                name = $"{name}_{Index}";
            }

            if (CurrentEntry == null)
            {
                CreateEntry();
            }

            CurrentEntry.Add(new JProperty(name, value));
        }

        private void AddPropertyIfNotNull(string name, string value)
        {
            if (value != null)
            {
                AddProperty(name, value);
            }
        }

        private void AddPropertyWithIntMap(string name, string str)
        {
            if (!_stringToInt.TryGetValue(str, out var key))
            {
                lock (_stringToInt)
                {
                    key = _stringToInt.IsEmpty ? 1 : _stringToInt.Values.Max() + 1;
                    _stringToInt.TryAdd(str, key);
                    AddProperty(name, key.ToString());
                }
            }
            else
            {
                AddProperty(name, key.ToString());
            }
        }

        private void AddPropertyIfNotNullWithIntMap(string name, string value)
        {
            if (value != null)
            {
                AddPropertyWithIntMap(name, value);
            }
        }
    }
}
