import React, { FC, ReactElement } from 'react';
import { MsalProvider, MsalAuthenticationTemplate } from "@azure/msal-react";
import { IPublicClientApplication, InteractionType } from "@azure/msal-browser";
import { ThemeProvider, IStackStyles } from '@fluentui/react';
import { initializeIcons } from '@fluentui/react/lib/Icons';

// MSAL imports
import { PublicClientApplication, EventType, EventMessage, AuthenticationResult } from "@azure/msal-browser";
import { msalConfig } from "./Config";

import ProvideAppContext, { useAppContext } from './AppContext';
import Home from "./pages/Home";
import Header from "./components/Header"
import { scopes } from './Config';
import AuthLoading from "./components/authLoading"
import AuthError from "./components/authError"
import { Stack } from "@fluentui/react";
export const msalInstance = new PublicClientApplication(msalConfig);

const appStyle: IStackStyles = {
  root: {
      width: '800px'
  }
}

export const App: FC = () => {
  
  // Check if there are already accounts in the browser session
  // If so, set the first account as the active account
  const accounts = msalInstance.getAllAccounts();
  if (accounts && accounts.length > 0) {
    msalInstance.setActiveAccount(accounts[0]);
  }

  msalInstance.addEventCallback((event: EventMessage) => {
    if (event.eventType === EventType.LOGIN_SUCCESS && event.payload) {
      // Set the active account - this simplifies token acquisition
      const authResult = event.payload as AuthenticationResult;
      msalInstance.setActiveAccount(authResult.account);
    }
  });

  const authRequest = {
    scopes: scopes
  };

  initializeIcons();

  return (
    <MsalProvider instance={msalInstance}>
      <MsalAuthenticationTemplate
        interactionType={InteractionType.Redirect}
        authenticationRequest={authRequest}
        errorComponent={AuthError}
        loadingComponent={AuthLoading}
      >
        <ThemeProvider>
          <ProvideAppContext>
              <Stack styles={appStyle}>
                <Stack.Item>
                  <Header></Header>
                </Stack.Item>
                <Stack.Item grow={1}>
                  <div></div>
                </Stack.Item>
                <Stack.Item grow={1}>
                  <Home />
                </Stack.Item>
              </Stack>
          </ProvideAppContext>
        </ThemeProvider>
      </MsalAuthenticationTemplate>
    </MsalProvider>
  );
}