import { FontIcon, getTheme, IconButton, IIconProps, IStackStyles, mergeStyles, Persona, PersonaSize, Stack, Text } from '@fluentui/react';
import React, { FC, ReactElement } from 'react';

const theme = getTheme();

const titleStyle: IStackStyles = {
    root: {
        width: '80%',
        // background: theme.palette.themePrimary,
        alignItems: 'center',
        padding: '0 20px'
    }
}

const logoIconClass = mergeStyles({
    fontSize: 24,
    paddingRight: 10,
    paddingTop: 4,
});

const iconProps: IIconProps = {
    styles: {
        root: {
            fontSize: 16,
            color: theme.palette.white
        }
    }
}

const Header: FC = (): ReactElement => {
    return (
        <Stack horizontal>
            <Stack horizontal styles={titleStyle}>
                <FontIcon aria-label="Check" iconName="Calories" className={logoIconClass} />
                <Text variant="xxLarge">Sample FHIR Context App</Text>
            </Stack>
            {
            /*<Stack.Item grow={1}>
                <div></div>
            </Stack.Item>
            <Stack.Item>
                <Stack horizontal grow={1}>
                <Persona size={PersonaSize.size32} text="Sample User" />
                </Stack>
            </Stack.Item>*/
            }
        </Stack>
    );
}

export default Header;