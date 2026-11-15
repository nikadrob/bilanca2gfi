import { AppShell, Burger, Group, Image, NavLink, Text } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { NavLink as RouterNavLink, Outlet, useLocation } from 'react-router';
import { features } from '../features/registry';

/** Header (logo + name), navigation on the left, the selected function's form on the right. */
export function AppLayout() {
  const [opened, { toggle, close }] = useDisclosure();
  const location = useLocation();

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
          {/* Placeholder for the user menu / login once authentication is added. */}
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
          />
        ))}
      </AppShell.Navbar>

      <AppShell.Main bg="var(--mantine-color-gray-0)">
        <Outlet />
      </AppShell.Main>
    </AppShell>
  );
}
