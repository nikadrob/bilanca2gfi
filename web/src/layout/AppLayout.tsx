import { ActionIcon, AppShell, Burger, Center, Group, Image, Loader, NavLink, Text, Tooltip } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { IconLogout, IconUser } from '@tabler/icons-react';
import { NavLink as RouterNavLink, Outlet, useLocation } from 'react-router';
import { useAuth } from '../auth/AuthContext';
import { LoginModal } from '../auth/LoginModal';
import { features } from '../features/registry';

/** Header (logo + name + user), navigation on the left, the selected function's form on the right. */
export function AppLayout() {
  const [opened, { toggle, close }] = useDisclosure();
  const location = useLocation();
  const { user, logout } = useAuth();

  return (
    <AppShell
      header={{ height: 60 }}
      navbar={{ width: 240, breakpoint: 'sm', collapsed: { mobile: !opened } }}
      padding="lg"
    >
      <AppShell.Header>
        <Group h="100%" px="md" gap="sm">
          <Burger opened={opened} onClick={toggle} hiddenFrom="sm" size="sm" aria-label="Izbornik" />
          <Image src="/logo.svg" alt="Calix-c" h={36} w={36} />
          <Text size="lg" className="app-title">
            Calix-c Helper
          </Text>
          {user && (
            <Group ml="auto" gap="xs">
              <IconUser size={18} stroke={1.6} />
              <Text size="sm" visibleFrom="xs">
                {user.username}
              </Text>
              <Tooltip label="Odjava">
                <ActionIcon variant="subtle" color="gray" onClick={logout} aria-label="Odjava">
                  <IconLogout size={18} />
                </ActionIcon>
              </Tooltip>
            </Group>
          )}
        </Group>
      </AppShell.Header>

      <AppShell.Navbar p="sm">
        {features.map((f) => (
          <NavLink
            key={f.path}
            component={RouterNavLink}
            to={f.path}
            label={f.label}
            description={f.description}
            leftSection={<f.icon size={20} stroke={1.6} />}
            active={location.pathname.startsWith(f.path)}
            onClick={close}
            disabled={!user}
          />
        ))}
      </AppShell.Navbar>

      <AppShell.Main bg="var(--mantine-color-gray-0)">
        {user === undefined ? (
          <Center h={200}>
            <Loader />
          </Center>
        ) : user === null ? (
          <LoginModal />
        ) : (
          <Outlet />
        )}
      </AppShell.Main>
    </AppShell>
  );
}
