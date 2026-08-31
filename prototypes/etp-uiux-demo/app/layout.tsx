import type { Metadata } from 'next';
import './globals.css';

export const metadata: Metadata = {
  title: 'ETP Reporting Engine · UX Prototype',
  description: 'Interactive task-first prototype for the ETP Reporting Engine desktop experience.',
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
