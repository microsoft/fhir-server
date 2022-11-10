// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

import { Client } from '@microsoft/microsoft-graph-client';
import { Application, OAuth2PermissionGrant, ServicePrincipal, User } from '@microsoft/microsoft-graph-types';
import { scopes } from './Config';
import { msalInstance } from "./index";

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

  try
  {
    // Return the /me API endpoint result as a User object
    user = await graphClient!.api('/me')
      // Only retrieve the specific fields needed
      .select('displayName,mail,mailboxSettings,userPrincipalName,id')
      .get();
  }
  catch (err : any)
  {
    // App won't have info for guest users
    if (err.body.includes("AadGuestPft"))
    {
      const account = msalInstance.getActiveAccount();
      return {
        id: account?.localAccountId,
        displayName: account?.name ?? "Guest User"
      };
    }
    
    throw err;
  }

  return user;
}

export async function getApplication(appId: string): Promise<Application | undefined> {
  await ensureClient();

  const app = await graphClient?.api(`/applications?`)
    .query(`$search="appId%3A${appId}"`)
    .header('ConsistencyLevel', 'eventual')
    .get();

  if (app.value.length == 1)
  {
    return app.value[0]; 
  }
}

export async function getServicePrincipal(appId: string): Promise<ServicePrincipal | undefined> {
  await ensureClient();

  const app = await graphClient?.api(`/servicePrincipals?`)
    .query(`$search="appId%3A${appId}"`)
    .header('ConsistencyLevel', 'eventual')
    .select('id,displayName,appRoles,oauth2PermissionScopes')
    .get();

  if (app.value.length == 1)
  {
    return app.value[0]; 
  }
}

export async function getAppCurrentScopes(appObjectId: string, userId: string): Promise<OAuth2PermissionGrant[]> {
  await ensureClient();

  const permissionList = await graphClient?.api(`/oauth2PermissionGrants`)
    .filter(`clientId eq '${appObjectId}' and principalId eq '${userId}'`)
    .get();

  return permissionList.value;
}