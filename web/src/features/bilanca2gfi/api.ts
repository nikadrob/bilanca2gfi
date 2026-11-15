export type Severity = 'Error' | 'Warning' | 'Info';

export interface Diagnostic {
  severity: Severity;
  area: string;
  message: string;
}

export interface GfiTotals {
  aktiva: number;
  pasiva: number;
  aktivaPrethodna: number;
  prihodi: number;
  rashodi: number;
  dobitPrijePoreza: number;
  porez: number;
  dobitRazdoblja: number;
}

export interface GeneratedFile {
  name: string;
  url: string;
}

export interface Bilanca2GfiResult {
  jobId: string;
  company: string;
  dateFrom: string | null;
  dateTo: string | null;
  accountCount: number;
  configName: string;
  pdfFile: string;
  priorFile: string | null;
  totals: GfiTotals;
  errorCount: number;
  warningCount: number;
  diagnostics: Diagnostic[];
  xls: GeneratedFile;
  report: GeneratedFile;
}

export interface Bilanca2GfiInput {
  pdf: File;
  prethodniGfi: File | null;
  porez: number | null;
  zaposleni: number | null;
  zaposleniSati: number | null;
}

export async function createGfi(input: Bilanca2GfiInput): Promise<Bilanca2GfiResult> {
  const body = new FormData();
  body.append('pdf', input.pdf);
  if (input.prethodniGfi) body.append('prethodniGfi', input.prethodniGfi);
  if (input.porez != null) body.append('porez', String(input.porez));
  if (input.zaposleni != null) body.append('zaposleni', String(input.zaposleni));
  if (input.zaposleniSati != null) body.append('zaposleniSati', String(input.zaposleniSati));

  const response = await fetch('/api/bilanca2gfi', { method: 'POST', body });
  if (!response.ok) {
    // The API answers with RFC 7807 problem details; "title" carries the user-facing message.
    let message = `Greška ${response.status}`;
    try {
      const problem = await response.json();
      message = problem.title ?? problem.detail ?? message;
    } catch {
      /* not JSON */
    }
    throw new Error(message);
  }
  return response.json();
}
