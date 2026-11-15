import type { ComponentType } from 'react';
import { IconFileSpreadsheet } from '@tabler/icons-react';
import { Bilanca2GfiPage } from './bilanca2gfi/Bilanca2GfiPage';

/**
 * Functions of the application. Each entry becomes a navigation item and a route.
 * To add a new function: create its page under src/features/<name>/ and add an entry here.
 */
export interface Feature {
  path: string;
  label: string;
  description: string;
  icon: ComponentType<{ size?: number | string; stroke?: number }>;
  element: ComponentType;
}

export const features: Feature[] = [
  {
    path: '/bilanca2gfi',
    label: 'Bilanca2Gfi',
    description: 'Bruto bilanca (PDF) → GFI-POD obrazac (.xls)',
    icon: IconFileSpreadsheet,
    element: Bilanca2GfiPage,
  },
];
