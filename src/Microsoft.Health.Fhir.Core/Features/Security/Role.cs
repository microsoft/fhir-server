// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;

namespace Microsoft.Health.Fhir.Core.Features.Security
{
    public class Role : IValidatableObject
    {
        private ResourceActions? _combinedActions;

        public Role(string name, ResourceActions combinedActions)
        {
            Name = name;
            _combinedActions = combinedActions;
        }

        public Role()
        {
            Actions = new ObservableCollection<ResourceActions>();
            NotActions = new ObservableCollection<ResourceActions>();

            Actions.CollectionChanged += (sender, e) => _combinedActions = null;
            NotActions.CollectionChanged += (sender, e) => _combinedActions = null;
        }

        // Config binding adds to collection
        private ObservableCollection<ResourceActions> Actions { get; }

        private ObservableCollection<ResourceActions> NotActions { get; }

        public string Name { get; private set; }

        public ResourceActions CombinedActions
        {
            get
            {
                if (_combinedActions != null)
                {
                    return _combinedActions.Value;
                }

                ResourceActions combinedActions = Actions.Aggregate(default(ResourceActions), (acc, a) => acc | a) &
                                                  ~NotActions.Aggregate(default(ResourceActions), (acc, a) => acc | a);

                _combinedActions = combinedActions;
                return combinedActions;
            }
        }

        public virtual IList<string> Scopes { get; internal set; } = new List<string>();

        public virtual IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                yield return new ValidationResult(Core.Resources.RoleNameEmpty);
            }

            if (Scopes.Count == 0 || Scopes.Any(s => s != "/"))
            {
                yield return new ValidationResult(string.Format(CultureInfo.InvariantCulture, Core.Resources.RoleScopeMustBeRoot, Name));
            }
        }
    }
}
