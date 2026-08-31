export type Role = 'Viewer' | 'Store Manager' | 'Owner';
export type ModuleId = 'today' | 'reports' | 'imports' | 'accounting' | 'archive' | 'exceptions' | 'settings';
export type ScreenKind = 'overview' | 'report' | 'workflow' | 'form' | 'table' | 'settings' | 'locked' | 'coverage';

export type PrototypeScreen = {
  id: string;
  label: string;
  module: ModuleId;
  group: string;
  kind: ScreenKind;
  description: string;
  minimumRole?: Role;
  available?: boolean;
  unavailableReason?: string;
  functionIds?: string[];
};

export type ModuleDefinition = {
  id: ModuleId;
  label: string;
  description: string;
};

export const modules: ModuleDefinition[] = [
  { id: 'today', label: 'Today', description: 'Daily close, readiness and performance' },
  { id: 'reports', label: 'Reports', description: '29 governed reports and packs' },
  { id: 'imports', label: 'Imports', description: 'Files, source documents and registers' },
  { id: 'accounting', label: 'Accounting', description: 'Balanced batches and Tally export' },
  { id: 'archive', label: 'Archive', description: 'Immutable generations and sharing' },
  { id: 'exceptions', label: 'Exceptions', description: 'Issues, controls and approvals' },
  { id: 'settings', label: 'Settings', description: 'Administration and system health' },
];

const descriptions: Record<ModuleId, string> = {
  today: 'Complete the business day with clear readiness, controls and next actions.',
  reports: 'Review trusted business results with shared filters, lineage and exports.',
  imports: 'Bring ETP and supporting documents in safely with visible quality checks.',
  accounting: 'Prepare a balanced, reviewable accounting batch before export.',
  archive: 'Find immutable report generations, compare restatements and share safely.',
  exceptions: 'Resolve the most important data and workflow issues in one queue.',
  settings: 'Manage governed configuration, access, resilience and audit records.',
};

function screen(
  id: string,
  label: string,
  module: ModuleId,
  group: string,
  kind: ScreenKind = 'table',
  options: Partial<PrototypeScreen> = {},
): PrototypeScreen {
  return {
    id,
    label,
    module,
    group,
    kind,
    description: descriptions[module],
    available: true,
    functionIds: [`navigation:${id}`],
    ...options,
  };
}

const todayScreens: PrototypeScreen[] = [
  screen('dashboard', 'Today overview', 'today', 'Overview', 'overview', { functionIds: ['module:dashboard', 'navigation:dashboard', 'route:dashboard'] }),
  screen('daily-health', 'Daily health', 'today', 'Business day', 'workflow'),
  screen('manual-entry', 'Manual entry', 'today', 'Business day', 'form', { minimumRole: 'Store Manager' }),
  screen('readiness', 'Reporting readiness', 'today', 'Business day', 'workflow'),
  screen('finalisation', 'Finalisation status', 'today', 'Business day', 'workflow'),
  screen('trends', 'Performance trends', 'today', 'Performance', 'report'),
  screen('store-comparison', 'Store comparison', 'today', 'Performance', 'report'),
  screen('target-progress', 'Target progress', 'today', 'Performance', 'report'),
  screen('control-summary', 'Control summary', 'today', 'Controls', 'table'),
  screen('recent-activity', 'Recent activity', 'today', 'Controls', 'table'),
];

const reportDefinitions = [
  ['dsr', 'Daily Sales / DSR', 'Sales'],
  ['sales-titan', 'Titan Sales Summary', 'Sales'],
  ['sales-helios', 'Helios Sales Summary', 'Sales'],
  ['sales-combined', 'Combined Sales Summary', 'Sales'],
  ['invoice', 'Invoice Summary', 'Sales'],
  ['sales-returns', 'Returns', 'Sales'],
  ['sales-brand', 'Brand-wise Sales', 'Sales'],
  ['sales-segment', 'Brand-Segment Sales', 'Sales'],
  ['sales-item', 'Item-wise Sales', 'Sales'],
  ['stock-closing', 'Closing Stock', 'Stock'],
  ['stock-physical', 'Physical Stock', 'Stock'],
  ['stock-variance', 'Stock Variance', 'Stock'],
  ['stock-movement', 'Stock Movement', 'Stock'],
  ['stock-group', 'Inventory-Group Report', 'Stock'],
  ['stock-brand', 'Brand Stock', 'Stock'],
  ['stock-slow', 'Slow / Exception Stock', 'Stock'],
  ['staff', 'Staff/CRO Performance', 'Staff'],
  ['tender', 'Tender Reconciliation', 'Tender & cash'],
  ['cash', 'Daily Cash Reconciliation', 'Tender & cash'],
  ['tender-diagnostic', 'Tender Diagnostics', 'Tender & cash'],
  ['service', 'Service Sales', 'Service'],
  ['exceptions', 'Daily Exception Report', 'Exceptions'],
  ['exception-source', 'Missing Source Report', 'Exceptions'],
  ['exception-unmapped', 'Unmapped Data', 'Exceptions'],
  ['exception-stock', 'Stock Exceptions', 'Exceptions'],
  ['exception-staff', 'Staff Exceptions', 'Exceptions'],
  ['exception-tender', 'Tender Exceptions', 'Exceptions'],
  ['management-trend', 'Management Trend', 'Management'],
  ['invoice-lineage', 'Invoice Source Drill-down', 'Investigation'],
] as const;

const reportScreens: PrototypeScreen[] = [
  screen('reports-home', 'Reports overview', 'reports', 'Overview', 'overview', { functionIds: ['module:reports', 'navigation:reports-home', 'route:sales-reports', 'route:stock-reports'] }),
  ...reportDefinitions.map(([id, label, group]) => screen(`report-${id}`, label, 'reports', group, 'report', {
    functionIds: [`navigation:report-${id}`, `report:${id}`],
  })),
  screen('store-daily-pack', 'Store daily pack', 'reports', 'Report packs', 'workflow'),
  screen('combined-pack', 'Combined pack', 'reports', 'Report packs', 'workflow'),
  screen('historical-packs', 'Historical packs', 'reports', 'Report packs', 'table'),
  screen('category-sales', 'Category-wise Sales', 'reports', 'Data-dependent future', 'locked', { available: false, unavailableReason: 'Requires an approved product-category master.' }),
  screen('sell-through', 'Sell-through', 'reports', 'Data-dependent future', 'locked', { available: false, unavailableReason: 'Requires an approved purchase/receipt source.' }),
  screen('stock-turn', 'Stock turn', 'reports', 'Data-dependent future', 'locked', { available: false, unavailableReason: 'Requires an approved purchase/receipt source.' }),
  screen('days-cover', 'Days of cover', 'reports', 'Data-dependent future', 'locked', { available: false, unavailableReason: 'Requires an approved replenishment policy.' }),
];

const importScreens: PrototypeScreen[] = [
  screen('import-overview', 'Import overview', 'imports', 'Overview', 'overview', { minimumRole: 'Store Manager', functionIds: ['module:imports', 'navigation:import-overview', 'route:import-etp'] }),
  screen('import-files', 'Import files', 'imports', 'Intake', 'workflow', { minimumRole: 'Store Manager' }),
  screen('source-inbox', 'Source inbox', 'imports', 'Intake', 'table'),
  screen('bulk-import', 'Bulk historical import', 'imports', 'Intake', 'workflow', { minimumRole: 'Store Manager' }),
  screen('watch-folder', 'Watch folder', 'imports', 'Intake', 'settings', { minimumRole: 'Owner' }),
  screen('quarantine', 'Quarantine', 'imports', 'Quality', 'table'),
  screen('duplicates', 'Exact duplicates', 'imports', 'Quality', 'table'),
  screen('already-present', 'Already-present facts', 'imports', 'Quality', 'table'),
  screen('conflicts', 'Conflict review', 'imports', 'Quality', 'table'),
  screen('import-failures', 'Import failures', 'imports', 'Quality', 'table'),
  screen('import-history', 'Import history', 'imports', 'Quality', 'table'),
  screen('documents', 'Document repository', 'imports', 'Documents & OCR', 'table'),
  screen('native-pdf', 'Native PDF extraction', 'imports', 'Documents & OCR', 'workflow'),
  screen('ocr-review', 'OCR review queue', 'imports', 'Documents & OCR', 'table'),
  screen('extraction-history', 'Extraction history', 'imports', 'Documents & OCR', 'table'),
  ...[
    ['register-inward', 'Inward register'],
    ['register-outward', 'Outward register'],
    ['register-credit', 'Credit note register'],
    ['register-service', 'Service receipt register'],
    ['register-transfer', 'Stock transfer register'],
    ['register-expense', 'Expense register'],
    ['register-vendor', 'Vendor invoice register'],
  ].map(([id, label]) => screen(id, label, 'imports', 'Digital registers', 'form', { minimumRole: 'Store Manager' })),
  screen('register-courier', 'Courier register', 'imports', 'Digital registers', 'locked', { available: false, unavailableReason: 'Register schema is not yet configured.' }),
];

const accountingScreens: PrototypeScreen[] = [
  screen('accounting-overview', 'Accounting overview', 'accounting', 'Accounting workflow', 'overview', { functionIds: ['module:accounting', 'navigation:accounting-overview', 'route:accounting'] }),
  screen('prepare-batch', 'Prepare batch', 'accounting', 'Accounting workflow', 'workflow'),
  screen('ledger-mapping', 'Ledger mapping', 'accounting', 'Accounting workflow', 'table', { minimumRole: 'Owner' }),
  screen('mapping-review', 'Mapping review', 'accounting', 'Accounting workflow', 'table'),
  screen('validation', 'Validation', 'accounting', 'Accounting workflow', 'workflow'),
  screen('tally-export', 'Tally export', 'accounting', 'Accounting workflow', 'workflow'),
  screen('export-history', 'Export history', 'accounting', 'Accounting workflow', 'table'),
  screen('accounting-reconciliation', 'Accounting reconciliation', 'accounting', 'Accounting workflow', 'report'),
  screen('direct-posting', 'Approved direct posting', 'accounting', 'Future extension', 'locked', { available: false, unavailableReason: 'No direct-posting authority exists.' }),
  screen('gst-assist', 'GST return assist', 'accounting', 'Future extension', 'locked', { available: false, unavailableReason: 'Requires an approved tax policy and source contract.' }),
];

const archiveScreens: PrototypeScreen[] = [
  screen('archive-overview', 'Archive overview', 'archive', 'Archive', 'overview', { functionIds: ['module:archive', 'navigation:archive-overview', 'route:report-archive'] }),
  screen('generations', 'Report generations', 'archive', 'Archive', 'table'),
  screen('final-packs', 'Finalised packs', 'archive', 'Archive', 'table'),
  screen('restatements', 'Restatements', 'archive', 'Archive', 'table'),
  screen('compare', 'Compare generations', 'archive', 'Archive', 'report'),
  screen('re-export', 'Re-export', 'archive', 'Archive', 'workflow'),
  screen('shared', 'Shared reports', 'archive', 'Archive', 'table'),
  screen('source-documents', 'Source documents', 'archive', 'Archive', 'table'),
];

const exceptionScreens: PrototypeScreen[] = [
  screen('open-items', 'Open items', 'exceptions', 'Exceptions & approvals', 'overview', { functionIds: ['module:exceptions', 'navigation:open-items', 'route:operations-center'] }),
  screen('data-quality', 'Data quality', 'exceptions', 'Exceptions & approvals', 'table'),
  screen('missing-sources', 'Missing sources', 'exceptions', 'Exceptions & approvals', 'table'),
  screen('unknown-layouts', 'Unknown layouts', 'exceptions', 'Exceptions & approvals', 'table'),
  screen('unmapped', 'Unmapped data', 'exceptions', 'Exceptions & approvals', 'table'),
  screen('import-conflicts', 'Import conflicts', 'exceptions', 'Exceptions & approvals', 'table'),
  screen('tender-exceptions', 'Tender exceptions', 'exceptions', 'Exceptions & approvals', 'table'),
  screen('stock-exceptions', 'Stock exceptions', 'exceptions', 'Exceptions & approvals', 'table'),
  screen('staff-exceptions', 'Staff exceptions', 'exceptions', 'Exceptions & approvals', 'table'),
  screen('ocr-exceptions', 'OCR review', 'exceptions', 'Exceptions & approvals', 'table'),
  screen('accounting-exceptions', 'Accounting exceptions', 'exceptions', 'Exceptions & approvals', 'table'),
  screen('approval-centre', 'Approval centre', 'exceptions', 'Exceptions & approvals', 'workflow', { minimumRole: 'Owner' }),
];

const settingsScreens: PrototypeScreen[] = [
  screen('settings', 'General settings', 'settings', 'Settings & admin', 'settings', { minimumRole: 'Owner', functionIds: ['module:health', 'navigation:settings', 'route:settings', 'route:admin-settings'] }),
  screen('users', 'Users & roles', 'settings', 'Settings & admin', 'table', { minimumRole: 'Owner' }),
  screen('stores', 'Stores', 'settings', 'Settings & admin', 'settings', { minimumRole: 'Owner' }),
  screen('masters', 'Master data', 'settings', 'Settings & admin', 'table', { minimumRole: 'Owner' }),
  screen('profiles', 'Import profiles', 'settings', 'Settings & admin', 'table', { minimumRole: 'Owner' }),
  screen('kpi', 'KPI catalogue', 'settings', 'Settings & admin', 'table', { minimumRole: 'Owner' }),
  screen('tender-rules', 'Tender rules', 'settings', 'Settings & admin', 'settings', { minimumRole: 'Owner' }),
  screen('accounting-map', 'Accounting mapping', 'settings', 'Settings & admin', 'table', { minimumRole: 'Owner' }),
  screen('watch', 'Watch folders', 'settings', 'Settings & admin', 'settings', { minimumRole: 'Owner' }),
  screen('ocr', 'OCR', 'settings', 'Settings & admin', 'settings', { minimumRole: 'Owner' }),
  screen('sharing', 'Email & sharing', 'settings', 'Settings & admin', 'settings', { minimumRole: 'Owner' }),
  screen('backup', 'Backup & recovery', 'settings', 'Settings & admin', 'workflow', { minimumRole: 'Owner' }),
  screen('scheduler', 'Scheduler', 'settings', 'Settings & admin', 'settings', { minimumRole: 'Owner' }),
  screen('health', 'System health', 'settings', 'Settings & admin', 'overview', { minimumRole: 'Owner' }),
  screen('audit', 'Audit trail', 'settings', 'Settings & admin', 'table', { minimumRole: 'Owner' }),
];

export const prototypeScreens: PrototypeScreen[] = [
  ...todayScreens,
  ...reportScreens,
  ...importScreens,
  ...accountingScreens,
  ...archiveScreens,
  ...exceptionScreens,
  ...settingsScreens,
  screen('prototype-coverage', 'Prototype coverage', 'settings', 'Audit', 'coverage', {
    minimumRole: 'Viewer',
    functionIds: [],
    description: 'Trace every audited function to its prototype destination and review deferred items.',
  }),
];

export const reportCodes = new Set(reportDefinitions.map(([code]) => `report-${code}`));

export const roleRank: Record<Role, number> = { Viewer: 0, 'Store Manager': 1, Owner: 2 };

export function isVisibleToRole(screen: PrototypeScreen, role: Role) {
  return roleRank[role] >= roleRank[screen.minimumRole ?? 'Viewer'];
}

export function screenForFunction(id: string): string {
  const direct = prototypeScreens.find((item) => item.functionIds?.includes(id));
  if (direct) return direct.id;
  if (id.startsWith('navigation:')) {
    const candidate = id.slice('navigation:'.length);
    if (prototypeScreens.some((item) => item.id === candidate)) return candidate;
  }
  if (id.startsWith('report:')) return `report-${id.slice('report:'.length)}`;
  const lower = id.toLowerCase();
  if (lower.includes('import') || lower.includes('sourceinbox') || lower.includes('register')) return 'import-overview';
  if (lower.includes('account')) return 'accounting-overview';
  if (lower.includes('archive') || lower.includes('distribution') || lower.includes('sharing')) return 'archive-overview';
  if (lower.includes('approval') || lower.includes('operation') || lower.includes('investigation')) return 'open-items';
  if (lower.includes('database') || lower.includes('admin') || lower.includes('startup')) return 'health';
  if (lower.includes('report') || lower.includes('tender') || lower.includes('stock') || lower.includes('staff')) return 'reports-home';
  return 'dashboard';
}

export const reportRows = [
  { reference: 'INV-260829-1042', description: 'Titan Edge automatic · GAUTO', store: 'HEMW', quantity: '1', value: '₹34,995', status: 'Matched' },
  { reference: 'INV-260829-1038', description: 'Helios smart wearable · HSMART', store: 'WLMHW', quantity: '2', value: '₹28,490', status: 'Matched' },
  { reference: 'INV-260829-1026', description: 'Titan Raga analog · LRAGA', store: 'HEMW', quantity: '1', value: '₹18,795', status: 'Matched' },
  { reference: 'SR-260829-0014', description: 'Sales return · signed value retained', store: 'WLMHW', quantity: '-1', value: '-₹12,400', status: 'Reviewed' },
  { reference: 'INV-260829-1019', description: 'Fastrack Reflex · FSMART', store: 'HEMW', quantity: '3', value: '₹15,597', status: 'Matched' },
];

export const issueRows = [
  { reference: 'EXC-1048', description: 'Walk-ins missing for WLMHW', store: 'WLMHW', quantity: 'Required', value: 'Daily close', status: 'Action' },
  { reference: 'EXC-1046', description: 'One new ledger mapping needs approval', store: 'HEMW', quantity: '1 mapping', value: 'Accounting', status: 'Review' },
  { reference: 'EXC-1039', description: 'OCR confidence below review threshold', store: 'HEMW', quantity: '2 fields', value: 'Source document', status: 'Review' },
];

export const auditFindings = [
  { priority: 'P1', title: 'Daily work is hidden behind module selection', change: 'Today opens with readiness and the single next action.' },
  { priority: 'P1', title: 'Navigation duplicates destinations and report favourites', change: 'One task rail plus a searchable contextual list.' },
  { priority: 'P1', title: 'Dense forms and equal-weight action rows obscure hierarchy', change: 'Progressive disclosure and one primary action per screen.' },
  { priority: 'P1', title: 'Loading can consume the whole report workspace', change: 'Non-blocking progress keeps filters and prior context visible.' },
  { priority: 'P2', title: 'Sidebar overlays compete with 960–1366 px content', change: 'Compact rail and responsive workflow panel preserve workspace width.' },
  { priority: 'P2', title: 'Role, permission and locked-day consequences are passive', change: 'Clear role state, reasons and safe unavailable previews.' },
  { priority: 'P2', title: 'Table drill-down is not visibly discoverable', change: 'Explicit row action and contextual detail drawer.' },
  { priority: 'P3', title: 'Status, empty and error treatments lack a shared rhythm', change: 'One consistent state language across every screen.' },
];
