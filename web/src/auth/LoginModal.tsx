import { useState, type FormEvent } from 'react';
import { Alert, Button, Group, Image, Modal, PasswordInput, Stack, Text, TextInput } from '@mantine/core';
import { IconAlertCircle, IconLogin } from '@tabler/icons-react';
import { useAuth } from './AuthContext';

/** Modal login form; cannot be dismissed until the user signs in. */
export function LoginModal() {
  const { login } = useAuth();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      await login(username, password);
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
      setPassword('');
    } finally {
      setBusy(false);
    }
  }

  return (
    <Modal
      opened
      onClose={() => undefined}
      withCloseButton={false}
      closeOnClickOutside={false}
      closeOnEscape={false}
      centered
      overlayProps={{ blur: 3 }}
      title={
        <Group gap="sm">
          <Image src="/logo.svg" alt="Calix-c" h={28} w={28} />
          <Text fw={600}>Prijava u Calix-c Helper</Text>
        </Group>
      }
    >
      <form onSubmit={submit}>
        <Stack>
          <TextInput
            label="Korisničko ime"
            value={username}
            onChange={(e) => setUsername(e.currentTarget.value)}
            autoComplete="username"
            data-autofocus
            required
          />
          <PasswordInput
            label="Lozinka"
            value={password}
            onChange={(e) => setPassword(e.currentTarget.value)}
            autoComplete="current-password"
            required
          />
          {error && (
            <Alert color="red" icon={<IconAlertCircle size={18} />} p="xs">
              {error}
            </Alert>
          )}
          <Button type="submit" loading={busy} leftSection={<IconLogin size={18} />} fullWidth>
            Prijavi se
          </Button>
        </Stack>
      </form>
    </Modal>
  );
}
