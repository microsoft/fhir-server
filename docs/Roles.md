# Roles in Microsoft FHIR Server for Azure

The FHIR server uses a role-based access control system. The access control model is based on the following concepts:

- **Data Actions** refer to specific allowed or disallowed operations that can be performed on a FHIR server's data. Examples include `read`, `write`, and `delete`.
- **Role definitions** or simply **roles**, are named collections of actions that are allowed be performed. They apply to a set of **scopes**.
- **Scopes** define the subset of data to which a role definition applies. Currently, only the root scope (`/`) is supported, which means that role definitions apply to all the data in the FHIR server.
- **Role assignments** grants a role definition to an identity (user, group, or service principal).

The set of data actions that can be part of a role definition are:

- `*` allows all data actions
- `read` is required for reading and searching resources.
- `write`is required for creating or updating resources.
- `delete` is required for deleting resources. Hard-deleting requires this in addition to `hardDelete`
- `hardDelete` is required, in addition to `delete`, for hard-deleting data.
- `export` is required for exporting data, in addition to `read`
- `resourceValidate` is required for invoking the [validate resource operation](https://www.hl7.org/fhir/operation-resource-validate.html).

Roles are defined in the [roles.json](../src/Microsoft.Health.Fhir.Shared.Web/roles.json) file. Administrators can customize them if desired. A role definition looks like this:

``` json
{
    "name": "globalWriter",
    "dataActions": [
        "*"
    ],
    "notDataActions": [
        "hardDelete"
    ],
    "scopes": [
        "/"
    ]
}
```

This role allows all data actions except `hardDelete`. Note that if a user is part of this role and another role that allows `hardDelete`, they will be allowed to perform the action.

Role assignments are done in the identity provider. In Azure Active Directory, you define app roles on the FHIR server's app registration. The app role names must correspond to the names of the roles defined in `roles.json`. Then you assign identities (users, groups, or service principals) to the app roles.
