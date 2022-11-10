import React, { FC, ReactElement } from 'react';
import { Stack, Text } from '@fluentui/react';


export const AuthLoading: FC = (): ReactElement => {
    return (
        <Stack>
            <Stack.Item>
            <Text block variant="xLarge">Loading</Text>
                <Text variant="small">Please wait...</Text>
            </Stack.Item>
        </Stack>
    )
}

export default AuthLoading;