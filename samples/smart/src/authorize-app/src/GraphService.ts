// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

import { Client, ResponseType } from '@microsoft/microsoft-graph-client';
import { Application, OAuth2PermissionGrant, ServicePrincipal, User } from '@microsoft/microsoft-graph-types';
import { scopes } from './Config';
import { msalInstance } from "./App";
import { resultContent } from '@fluentui/react/lib/components/FloatingPicker/PeoplePicker/PeoplePicker.scss';

let graphClient: Client | undefined = undefined;

async function ensureClient() {
  if (graphClient == undefined) {

    const account = msalInstance.getActiveAccount();
    if (!account) {
      throw Error("No active account! Verify a user has been signed in and setActiveAccount has been called.");
    }

    const response = await msalInstance.acquireTokenSilent({
      scopes: scopes,
      account: account
    });

    graphClient = Client.init({
      authProvider: (success) => {
        success(null, response.accessToken);
      }
    });
  }

  return graphClient;
}

export async function getUser(): Promise<User> {
  await ensureClient();

  let user: User;

  const test = msalInstance.getActiveAccount()

  // Return the /me API endpoint result as a User object
  const response = await graphClient!.api('/me')
    .responseType(ResponseType.RAW)
    .get();

  if (response.ok) {
    user = await response.json();
  } else {
    const errResponseText = await response.text();
    if (errResponseText.includes("AadGuestPft")) {
      const account = msalInstance.getActiveAccount();
      return {
        id: account?.localAccountId,
        displayName: account?.name ?? "Guest User"
      };
    }
    throw Error(`Graph API returned ${response.status}: ${response.statusText}`);
  }

  return user;
}

export async function getApplication(appId: string): Promise<Application | undefined> {
  await ensureClient();

  const result = await graphClient?.api(`/applications?`)
    .query(`$search="appId%3A${appId}"`)
    .header('ConsistencyLevel', 'eventual')
    .responseType(ResponseType.RAW)
    .get();

  if (result.status === 200) {
    const json = await result.json();

    if (json.value.length == 1) {
      return json.value[0];
    }
    else if (json.value.length > 1) {
        throw Error(`Multiple applications found with appId ${appId}`);
    }

    throw Error(`No application found with appId ${appId}`);
  }

  throw Error(`Error getting application with appId ${appId}: ${result.status} ${result.statusText}`);
}

export async function getServicePrincipal(appId: string): Promise<ServicePrincipal | undefined> {
  await ensureClient();

  const result = await graphClient?.api(`/servicePrincipals?`)
    .query(`$search="appId%3A${appId}"`)
    .header('ConsistencyLevel', 'eventual')
    .select('id,appId,displayName,appRoles,oauth2PermissionScopes')
    .responseType(ResponseType.RAW)
    .get();

  if (result.status == 200) {
    const json = await result.json();

    if (json.value.length == 1) {
      const sp = json.value[0];
      return sp;
    } else if (json.value.length > 1) {
      throw Error(`Multiple service principals found for appId ${appId}`);
    }
    throw Error(`No service principal found for app ${appId}`);
  }

  throw Error(`Error ${result.status} returned from Graph API Service Principal query.`);
}

export async function getAppCurrentScopes(appObjectId: string, userId: string): Promise<OAuth2PermissionGrant[]> {
  await ensureClient();

  const result = await graphClient?.api(`/oauth2PermissionGrants`)
    .filter(`clientId eq '${appObjectId}' and principalId eq '${userId}'`)
    .responseType(ResponseType.RAW)
    .get();

  if (result.status == 200) {
    const json = await result.json();
    return json.value;
  }

  throw Error(`Error ${result.status} returned from Graph API OAuth2PermissionGrant query.`);
}

export async function patchAppCurrentScopes(grantId: string, scope: string): Promise<void> {
  await ensureClient();

  /*const result = await graphClient?.api(`/oauth2PermissionGrants/${grantId}`)
  .responseType(ResponseType.RAW)
  .patch({"scope": scope});

  if (result.status == 204) {
    return
  }

  throw Error(`Error ${result.status} returned from Graph API OAuth2PermissionGrant update.`);*/
}

export async function createAppCurrentScopes(clientId: string, principalId: string, resourceId: string, scope: string): Promise<OAuth2PermissionGrant> {
  await ensureClient();

  const body : OAuth2PermissionGrant = {
    "clientId": clientId,
    "consentType": "Principal",
    "principalId": principalId,
    "resourceId": resourceId,
    "scope": scope
  };

/*#const result = await graphClient?.api(`/oauth2PermissionGrants`)
  .responseType(ResponseType.RAW)
  .post(body);

  if (result.status == 201) {
    return result.value;
  }

  throw Error(`Error ${result.status} returned from Graph API OAuth2PermissionGrant create.`);*/

  return new Promise<OAuth2PermissionGrant>((resolve, reject) => {
    setTimeout(() => {
      resolve({
        "clientId": clientId,
        "consentType": "Principal",
        "principalId": principalId,
        "resourceId": resourceId,
        "scope": scope,
        "id": "1234567890"
      });
    }, 1000);
  });
}
