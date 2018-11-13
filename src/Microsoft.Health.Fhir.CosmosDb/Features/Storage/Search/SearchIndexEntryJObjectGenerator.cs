// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Globalization;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.CosmosDb.Features.Search;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Search
{
    internal class SearchIndexEntryJObjectGenerator : ISearchValueVisitor
    {
        private List<JObject> _generatedObjects = new List<JObject>();

        public SearchIndexEntryJObjectGenerator()
        {
        }

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
            AddProperty(SearchValueConstants.DateTimeStartName, dateTime.Start.ToString("o", CultureInfo.InvariantCulture));
            AddProperty(SearchValueConstants.DateTimeEndName, dateTime.End.ToString("o", CultureInfo.InvariantCulture));
        }

        void ISearchValueVisitor.Visit(NumberSearchValue number)
        {
            AddProperty(SearchValueConstants.NumberName, number.Number);
        }

        void ISearchValueVisitor.Visit(QuantitySearchValue quantity)
        {
            AddPropertyIfNotNull(SearchValueConstants.SystemName, quantity.System);
            AddPropertyIfNotNull(SearchValueConstants.CodeName, quantity.Code);
            AddProperty(SearchValueConstants.QuantityName, quantity.Quantity);
        }

        void ISearchValueVisitor.Visit(ReferenceSearchValue reference)
        {
            AddPropertyIfNotNull(SearchValueConstants.ReferenceBaseUriName, reference.BaseUri?.ToString());
            AddPropertyIfNotNull(SearchValueConstants.ReferenceResourceTypeName, reference.ResourceType?.ToString());
            AddProperty(SearchValueConstants.ReferenceResourceIdName, reference.ResourceId);
        }

        void ISearchValueVisitor.Visit(StringSearchValue s)
        {
            if (!IsCompositeComponent)
            {
                AddProperty(SearchValueConstants.StringName, s.String);
            }

            AddProperty(SearchValueConstants.NormalizedStringName, s.String.ToUpperInvariant());
        }

        void ISearchValueVisitor.Visit(TokenSearchValue token)
        {
            AddPropertyIfNotNull(SearchValueConstants.SystemName, token.System);
            AddPropertyIfNotNull(SearchValueConstants.CodeName, token.Code);

            if (!IsCompositeComponent)
            {
                // Since text is case-insensitive search, it will always be normalized.
                AddPropertyIfNotNull(SearchValueConstants.NormalizedTextName, token.Text?.ToUpperInvariant());
            }
        }

        void ISearchValueVisitor.Visit(UriSearchValue uri)
        {
            AddProperty(SearchValueConstants.UriName, uri.Uri);
        }

        private void CreateEntry()
        {
            CurrentEntry = new JObject
            {
                new JProperty(SearchValueConstants.ParamName, Entry.ParamName),
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
    }
}
