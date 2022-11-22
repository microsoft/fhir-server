// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

import { scopes, apiEndpoint } from './Config';
import { msalInstance } from "./App";
import { AppConsentInfo } from './AppContext';
import { request } from 'http';

let token: string | undefined = undefined;

async function ensureToken() {
  if (token === undefined) {
    const account = msalInstance.getActiveAccount();
    if (!account) {
      throw Error("No active account! Verify a user has been signed in and setActiveAccount has been called.");
    }

    const response = await msalInstance.acquireTokenSilent({
      scopes: scopes,
      account: account
    });

    token = response.accessToken;
  }
}

interface HttpResponse<T> extends Response {
  parsedBody?: T;
}
interface HttpResponse<T> extends Response {
  parsedBody?: T;
}
export async function http<T>(
  request: RequestInfo
): Promise<HttpResponse<T>> {
  const response: HttpResponse<T> = await fetch(
    request
  );

  if (!response.ok) {
    throw Error(`Backend Application Consent API returned ${response.status}: ${response.statusText}`);
  }
  response.parsedBody = await response.json();
  return response;
}



export async function getAppConsentInfo(clientId: string, scopes: string): Promise<AppConsentInfo> {
  await ensureToken();

  let response: HttpResponse<AppConsentInfo>;

  try
  {
    // Return the /me API endpoint result as a User object
    response = await http<AppConsentInfo>(
      new Request(`${apiEndpoint}/api/contextInfo?clientId=${clientId}&scope=${scopes}`, {
      method: "GET",
      headers: {
        "Authorization": `Bearer ${token}`,
      }
    }));
    
    return response.json();
  }
  catch (error: any) {
    throw Error(`Fatal error while accessing Backend Application Consent API. Check your application settings: ${error}`);
  }
}