
import { FontIcon, getTheme, IconButton, IIconProps, IStackStyles, mergeStyles, Persona, PersonaSize, Stack, Text } from '@fluentui/react';

import { useAppContext } from '../AppContext';
import { UserInfo } from '../components/UserInfo'
import { AppInfo } from '../components/AppInfo'
import { ScopeSelector } from '../components/ScopeSelector'
import ErrorMessage from '../ErrorMessage';

const homeStyle: IStackStyles = {
  root: {
    paddingLeft: 30,
    paddingTop: 30,
  }
}

const moduleStyle: IStackStyles = {
    root: {
      paddingBottom: 20,
    }
  }

export default function Welcome() {
    const app = useAppContext();

    return (
        <Stack styles={homeStyle}>
            {
              !app.error &&
              <>
              <Stack.Item styles={moduleStyle}>
                  <UserInfo user={app.user} logout={app.logout}/>
              </Stack.Item>
      
              <Stack.Item styles={moduleStyle}>
                  <AppInfo appInfo={app.appInfo} />
              </Stack.Item>
              <Stack.Item styles={moduleStyle}>
                  <ScopeSelector 
                      authInfo={app.authInfo}
                      updateUserApprovedScopes={app.saveScopes}
                  />
              </Stack.Item>
            </>
    }
    {
      app.error &&
      <Stack.Item styles={moduleStyle}>
        <ErrorMessage></ErrorMessage>
        </Stack.Item>
    }
        </Stack>
    );
}