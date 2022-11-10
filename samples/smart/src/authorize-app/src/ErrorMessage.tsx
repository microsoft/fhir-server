import { Stack, Text, } from '@fluentui/react';
import { useAppContext } from './AppContext';

export default function ErrorMessage() {
  const app = useAppContext();

  if (app.error) {
    return (
    <Stack>
        <Stack.Item>
        <Text block variant="xLarge">Error</Text>
            <Text variant="small">{app.error.message}</Text>
        </Stack.Item>
    </Stack>
    );
  }

  return null;
}