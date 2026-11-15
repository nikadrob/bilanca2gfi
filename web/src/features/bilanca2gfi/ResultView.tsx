import { Accordion, Alert, Badge, Button, Card, Group, List, SimpleGrid, Stack, Table, Text, Title } from '@mantine/core';
import { IconAlertTriangle, IconCircleCheck, IconCircleX, IconDownload, IconFileText } from '@tabler/icons-react';
import { formatAmount, formatDate } from '../../lib/format';
import type { Bilanca2GfiResult, Diagnostic } from './api';

export function ResultView({ result: r }: { result: Bilanca2GfiResult }) {
  const errors = r.diagnostics.filter((d) => d.severity === 'Error');
  const warnings = r.diagnostics.filter((d) => d.severity === 'Warning');
  const infos = r.diagnostics.filter((d) => d.severity === 'Info');
  const t = r.totals;
  const balanced = t.aktiva === t.pasiva;

  return (
    <Card withBorder padding="lg">
      <Stack gap="lg">
        <Group justify="space-between" align="flex-start">
          <div>
            <Title order={3}>{r.company}</Title>
            <Text c="dimmed" size="sm">
              Razdoblje {formatDate(r.dateFrom)} – {formatDate(r.dateTo)} · {r.accountCount} konta · {r.configName}
            </Text>
          </div>
          <StatusBadge errors={r.errorCount} warnings={r.warningCount} />
        </Group>

        {/* Export */}
        <Card withBorder bg="var(--mantine-color-blue-0)">
          <Stack gap="xs">
            <Text fw={600}>Izrađene datoteke</Text>
            <Group gap="sm">
              <Button component="a" href={r.xls.url} download leftSection={<IconDownload size={18} />}>
                {r.xls.name}
              </Button>
              <Button component="a" href={r.report.url} download variant="light" leftSection={<IconFileText size={18} />}>
                Izvještaj o kontrolama
              </Button>
            </Group>
            <Text size="xs" c="dimmed">
              Ulaz: {r.pdfFile}
              {r.priorFile ? ` · prethodna godina: ${r.priorFile}` : ' · bez prošlogodišnjeg GFI-a'}
            </Text>
          </Stack>
        </Card>

        {/* Summary */}
        <SimpleGrid cols={{ base: 1, md: 2 }} spacing="lg">
          <Table withRowBorders={false} verticalSpacing={4}>
            <Table.Tbody>
              <Row label="Ukupno aktiva" value={t.aktiva} />
              <Row label="Ukupno pasiva" value={t.pasiva} />
              <Table.Tr>
                <Table.Td colSpan={2}>
                  <Text size="sm" c={balanced ? 'teal' : 'red'}>
                    {balanced ? 'Aktiva i pasiva su jednake.' : `Razlika: ${formatAmount(t.aktiva - t.pasiva)}`}
                  </Text>
                </Table.Td>
              </Table.Tr>
              <Row label="Aktiva prethodne godine" value={t.aktivaPrethodna} muted />
            </Table.Tbody>
          </Table>
          <Table withRowBorders={false} verticalSpacing={4}>
            <Table.Tbody>
              <Row label="Ukupni prihodi" value={t.prihodi} />
              <Row label="Ukupni rashodi" value={t.rashodi} />
              <Row label="Dobit prije oporezivanja" value={t.dobitPrijePoreza} />
              <Row label="Porez na dobit" value={t.porez} />
              <Row label="Dobit razdoblja" value={t.dobitRazdoblja} strong />
            </Table.Tbody>
          </Table>
        </SimpleGrid>

        {/* Controls */}
        {errors.length > 0 && <DiagnosticList title="Greške" color="red" items={errors} />}
        {warnings.length > 0 && <DiagnosticList title="Upozorenja" color="orange" items={warnings} />}
        {infos.length > 0 && (
          <Accordion variant="contained">
            <Accordion.Item value="info">
              <Accordion.Control>Provedene kontrole ({infos.length})</Accordion.Control>
              <Accordion.Panel>
                <List size="sm" spacing={4}>
                  {infos.map((d, i) => (
                    <List.Item key={i}>
                      <b>{d.area}:</b> {d.message}
                    </List.Item>
                  ))}
                </List>
              </Accordion.Panel>
            </Accordion.Item>
          </Accordion>
        )}
      </Stack>
    </Card>
  );
}

function Row({ label, value, strong, muted }: { label: string; value: number; strong?: boolean; muted?: boolean }) {
  return (
    <Table.Tr>
      <Table.Td>
        <Text size="sm" c={muted ? 'dimmed' : undefined} fw={strong ? 700 : undefined}>
          {label}
        </Text>
      </Table.Td>
      <Table.Td className="amount">
        <Text size="sm" c={muted ? 'dimmed' : undefined} fw={strong ? 700 : undefined}>
          {formatAmount(value)} €
        </Text>
      </Table.Td>
    </Table.Tr>
  );
}

function StatusBadge({ errors, warnings }: { errors: number; warnings: number }) {
  if (errors > 0)
    return (
      <Badge size="lg" color="red" leftSection={<IconCircleX size={14} />}>
        {errors} grešaka · {warnings} upozorenja
      </Badge>
    );
  if (warnings > 0)
    return (
      <Badge size="lg" color="orange" leftSection={<IconAlertTriangle size={14} />}>
        {warnings} upozorenja
      </Badge>
    );
  return (
    <Badge size="lg" color="teal" leftSection={<IconCircleCheck size={14} />}>
      Bez grešaka
    </Badge>
  );
}

function DiagnosticList({ title, color, items }: { title: string; color: string; items: Diagnostic[] }) {
  return (
    <Alert color={color} title={`${title} (${items.length})`} variant="light">
      <List size="sm" spacing={6}>
        {items.map((d, i) => (
          <List.Item key={i}>
            <b>{d.area}:</b> {d.message}
          </List.Item>
        ))}
      </List>
    </Alert>
  );
}
