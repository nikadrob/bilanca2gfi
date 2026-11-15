import { useState } from 'react';
import { Alert, Button, Card, FileInput, Grid, Group, NumberInput, Stack, Text, Title } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { IconAlertCircle, IconFileTypePdf, IconFileTypeXls, IconPlayerPlay } from '@tabler/icons-react';
import { createGfi, type Bilanca2GfiResult } from './api';
import { ResultView } from './ResultView';

const toNumber = (v: string | number) => (v === '' || v == null ? null : Number(v));

export function Bilanca2GfiPage() {
  const [pdf, setPdf] = useState<File | null>(null);
  const [prior, setPrior] = useState<File | null>(null);
  const [porez, setPorez] = useState<string | number>('');
  const [zaposleni, setZaposleni] = useState<string | number>('');
  const [zaposleniSati, setZaposleniSati] = useState<string | number>('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<Bilanca2GfiResult | null>(null);

  async function submit() {
    if (!pdf) {
      setError('Odaberi PDF bruto bilance.');
      return;
    }
    setBusy(true);
    setError(null);
    setResult(null);
    try {
      const r = await createGfi({
        pdf,
        prethodniGfi: prior,
        porez: toNumber(porez),
        zaposleni: toNumber(zaposleni),
        zaposleniSati: toNumber(zaposleniSati),
      });
      setResult(r);
      notifications.show({
        color: r.errorCount > 0 ? 'orange' : 'green',
        title: 'GFI je izrađen',
        message: r.errorCount > 0 ? `Kontrole javljaju ${r.errorCount} grešaka – pogledaj rezime.` : 'Kontrole su prošle bez grešaka.',
      });
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Stack maw={1100} gap="lg">
      <div>
        <Title order={2}>Bilanca2Gfi</Title>
        <Text c="dimmed">
          Iz bruto bilance po kontima (PDF) izrađuje FINA obrazac GFI-POD: Bilanca, RDG i Dodatni podaci.
        </Text>
      </div>

      <Card withBorder padding="lg">
        <Stack gap="md">
          <Grid gap="md">
            <Grid.Col span={{ base: 12, md: 6 }}>
              <FileInput
                label="Bruto bilanca (PDF)"
                description="Bruto bilanca po kontima iz računovodstvenog programa"
                placeholder="Odaberi PDF…"
                accept="application/pdf,.pdf"
                leftSection={<IconFileTypePdf size={18} />}
                value={pdf}
                onChange={setPdf}
                clearable
                withAsterisk
              />
            </Grid.Col>
            <Grid.Col span={{ base: 12, md: 6 }}>
              <FileInput
                label="Prošlogodišnji GFI (.xls)"
                description="Neobavezno: prethodna godina RDG-a, Dodatnih podataka i referentna stranica"
                placeholder="Odaberi .xls…"
                accept=".xls,application/vnd.ms-excel"
                leftSection={<IconFileTypeXls size={18} />}
                value={prior}
                onChange={setPrior}
                clearable
              />
            </Grid.Col>
            <Grid.Col span={{ base: 12, sm: 4 }}>
              <NumberInput
                label="Porez na dobit (EUR)"
                description="Prazno = 0"
                placeholder="0,00"
                value={porez}
                onChange={setPorez}
                min={0}
                decimalScale={2}
                fixedDecimalScale
                decimalSeparator=","
                thousandSeparator="."
                allowNegative={false}
                hideControls
              />
            </Grid.Col>
            <Grid.Col span={{ base: 12, sm: 4 }}>
              <NumberInput
                label="Prosječan broj zaposlenih"
                description="Tekuća godina"
                value={zaposleni}
                onChange={setZaposleni}
                min={0}
                decimalScale={2}
                decimalSeparator=","
                allowNegative={false}
              />
            </Grid.Col>
            <Grid.Col span={{ base: 12, sm: 4 }}>
              <NumberInput
                label="Broj zaposlenih prema satima rada"
                description="Tekuća godina"
                value={zaposleniSati}
                onChange={setZaposleniSati}
                min={0}
                decimalScale={2}
                decimalSeparator=","
                allowNegative={false}
              />
            </Grid.Col>
          </Grid>

          {error && (
            <Alert color="red" icon={<IconAlertCircle size={18} />} title="Obrada nije uspjela">
              {error}
            </Alert>
          )}

          <Group justify="flex-end">
            <Button leftSection={<IconPlayerPlay size={18} />} onClick={submit} loading={busy} disabled={!pdf}>
              Kreiraj
            </Button>
          </Group>
        </Stack>
      </Card>

      {result && <ResultView result={result} />}
    </Stack>
  );
}
