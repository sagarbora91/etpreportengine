'use client';

import { useEffect, useMemo, useRef, useState } from 'react';
import {
  AlertTriangle,
  Archive,
  ArrowRight,
  BarChart3,
  CalendarDays,
  Check,
  CheckCircle2,
  ChevronDown,
  CircleHelp,
  ClipboardCheck,
  Clock3,
  Database,
  Download,
  FileInput,
  FileSpreadsheet,
  FileText,
  FolderArchive,
  Home,
  Landmark,
  LayoutGrid,
  ListFilter,
  LockKeyhole,
  Menu,
  MoreHorizontal,
  PanelRightOpen,
  RefreshCw,
  Search,
  Settings,
  ShieldAlert,
  ShieldCheck,
  Sparkles,
  Store,
  Upload,
  UserRound,
  X,
} from 'lucide-react';

import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Card,
  CardAction,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import { Progress } from '@/components/ui/progress';
import coverage from '@/lib/function-coverage.json';
import {
  auditFindings,
  isVisibleToRole,
  issueRows,
  modules,
  prototypeScreens,
  reportCodes,
  reportRows,
  screenForFunction,
  type ModuleId,
  type PrototypeScreen,
  type Role,
} from '@/lib/prototype-data';

type DemoState = 'Ready' | 'Loading' | 'Empty' | 'Error' | 'Locked';
type Row = (typeof reportRows)[number];

const moduleIcons = {
  today: Home,
  reports: BarChart3,
  imports: FileInput,
  accounting: Landmark,
  archive: Archive,
  exceptions: AlertTriangle,
  settings: Settings,
} satisfies Record<ModuleId, typeof Home>;

const roles: Role[] = ['Viewer', 'Store Manager', 'Owner'];
const demoStates: DemoState[] = ['Ready', 'Loading', 'Empty', 'Error', 'Locked'];

const readiness = [
  { label: 'Sales files', detail: '6 of 6 imported', state: 'Ready', target: 'import-overview', icon: FileInput },
  { label: 'Control totals', detail: 'R022 and R025 agree', state: 'Passed', target: 'control-summary', icon: ShieldCheck },
  { label: 'Manual inputs', detail: 'Walk-ins still required', state: 'Action', target: 'manual-entry', icon: ClipboardCheck },
  { label: 'Daily pack', detail: 'Available after walk-ins', state: 'Waiting', target: 'store-daily-pack', icon: FileText },
];

function updateHash(screenId: string) {
  if (typeof window === 'undefined') return;
  window.history.replaceState(null, '', `#${screenId}`);
}

function groupBy<T>(items: readonly T[], keyFor: (item: T) => string): Record<string, T[]> {
  return items.reduce<Record<string, T[]>>((groups, item) => {
    const key = keyFor(item);
    (groups[key] ??= []).push(item);
    return groups;
  }, {});
}

export default function HomePage() {
  const [currentId, setCurrentId] = useState('dashboard');
  const [role, setRole] = useState<Role>('Store Manager');
  const [demoState, setDemoState] = useState<DemoState>('Ready');
  const [query, setQuery] = useState('');
  const [drawerRow, setDrawerRow] = useState<Row | null>(null);
  const [mobileNavigation, setMobileNavigation] = useState(false);
  const searchRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    const fromHash = window.location.hash.slice(1);
    const initialHashTimer = window.setTimeout(() => {
      if (prototypeScreens.some((item) => item.id === fromHash)) setCurrentId(fromHash);
    }, 0);
    const onHashChange = () => {
      const next = window.location.hash.slice(1);
      if (prototypeScreens.some((item) => item.id === next)) setCurrentId(next);
    };
    const onKeyDown = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault();
        searchRef.current?.focus();
      }
      if (event.key === 'Escape') {
        setDrawerRow(null);
        setMobileNavigation(false);
      }
    };
    window.addEventListener('hashchange', onHashChange);
    window.addEventListener('keydown', onKeyDown);
    return () => {
      window.clearTimeout(initialHashTimer);
      window.removeEventListener('hashchange', onHashChange);
      window.removeEventListener('keydown', onKeyDown);
    };
  }, []);

  const current = prototypeScreens.find((item) => item.id === currentId) ?? prototypeScreens[0];
  const currentModule = current.module;
  const contextualScreens = useMemo(() => {
    const items = prototypeScreens.filter((item) => item.module === currentModule && item.id !== 'prototype-coverage');
    if (!query.trim()) return items;
    const term = query.trim().toLowerCase();
    return prototypeScreens.filter((item) => `${item.label} ${item.group} ${item.module}`.toLowerCase().includes(term));
  }, [currentModule, query]);

  const navigate = (id: string) => {
    setCurrentId(id);
    setDrawerRow(null);
    setMobileNavigation(false);
    updateHash(id);
  };

  const openModule = (module: ModuleId) => {
    const first = prototypeScreens.find((item) => item.module === module && isVisibleToRole(item, role));
    if (first) navigate(first.id);
  };

  return (
    <main className="min-h-screen bg-background text-foreground">
      <div className="grid min-h-screen grid-cols-[72px_minmax(0,1fr)] xl:grid-cols-[72px_260px_minmax(0,1fr)]">
        <GlobalRail currentModule={currentModule} role={role} onModule={openModule} onCoverage={() => navigate('prototype-coverage')} />

        <ContextNavigation
          className="hidden xl:flex"
          module={currentModule}
          currentId={currentId}
          role={role}
          screens={contextualScreens}
          query={query}
          setQuery={setQuery}
          searchRef={searchRef}
          onNavigate={navigate}
        />

        {mobileNavigation ? (
          <dialog open className="fixed inset-0 z-40 m-0 flex h-screen w-screen max-w-none border-0 bg-ink/45 p-0 xl:hidden" aria-label="Workspace navigation">
            <ContextNavigation
              className="w-[min(350px,calc(100vw-48px))] shadow-2xl"
              module={currentModule}
              currentId={currentId}
              role={role}
              screens={contextualScreens}
              query={query}
              setQuery={setQuery}
              searchRef={searchRef}
              onNavigate={navigate}
              onClose={() => setMobileNavigation(false)}
            />
            <button type="button" aria-label="Close navigation" className="flex-1" onClick={() => setMobileNavigation(false)} />
          </dialog>
        ) : null}

        <section className="min-w-0">
          <TopBar
            role={role}
            setRole={setRole}
            state={demoState}
            setState={setDemoState}
            onMenu={() => setMobileNavigation(true)}
          />
          <div className="mx-auto max-w-[1540px] p-4 md:p-6 2xl:p-8">
            <ScreenHeader screen={current} role={role} onNavigate={navigate} />
            <ScreenState screen={current} role={role} state={demoState} onNavigate={navigate} onRow={setDrawerRow} />
          </div>
        </section>
      </div>

      {drawerRow ? <DetailDrawer row={drawerRow} onClose={() => setDrawerRow(null)} /> : null}
    </main>
  );
}

function GlobalRail({ currentModule, role, onModule, onCoverage }: { currentModule: ModuleId; role: Role; onModule: (id: ModuleId) => void; onCoverage: () => void }) {
  return (
    <aside className="sticky top-0 z-30 flex h-screen flex-col border-r border-sidebar-border bg-sidebar px-2.5 py-4 text-sidebar-foreground">
      <div className="mb-5 grid h-11 place-items-center">
        <div className="grid size-10 place-items-center rounded-xl bg-primary text-primary-foreground shadow-sm"><BarChart3 className="size-5" /></div>
      </div>
      <nav aria-label="Primary modules" className="space-y-1.5">
        {modules.map((module) => {
          const Icon = moduleIcons[module.id];
          const requiresOwner = module.id === 'settings';
          const unavailable = requiresOwner && role !== 'Owner';
          return (
            <button
              key={module.id}
              type="button"
              aria-label={module.label}
              aria-current={currentModule === module.id ? 'page' : undefined}
              disabled={unavailable}
              title={unavailable ? `${module.label} requires Owner access` : module.label}
              onClick={() => onModule(module.id)}
              className={`group relative grid size-[50px] place-items-center rounded-xl transition ${currentModule === module.id ? 'bg-sidebar-primary text-white shadow-sm' : 'text-sidebar-foreground/60 hover:bg-sidebar-accent hover:text-white'} disabled:cursor-not-allowed disabled:opacity-35`}
            >
              <Icon className="size-[19px]" />
              {module.id === 'exceptions' ? <span className="absolute right-1 top-1 grid size-4 place-items-center rounded-full bg-critical text-[9px] font-bold text-white">3</span> : null}
              <span className="pointer-events-none absolute left-[58px] z-50 hidden whitespace-nowrap rounded-lg bg-ink px-2.5 py-1.5 text-xs text-white shadow-lg group-hover:block">{module.label}</span>
            </button>
          );
        })}
      </nav>
      <div className="mt-auto space-y-1.5 border-t border-sidebar-border pt-3">
        <button type="button" onClick={onCoverage} aria-label="Prototype coverage" title="Prototype coverage" className="group relative grid size-[50px] place-items-center rounded-xl text-sidebar-foreground/60 hover:bg-sidebar-accent hover:text-white">
          <LayoutGrid className="size-[19px]" />
          <span className="pointer-events-none absolute left-[58px] z-50 hidden whitespace-nowrap rounded-lg bg-ink px-2.5 py-1.5 text-xs text-white shadow-lg group-hover:block">Prototype coverage</span>
        </button>
        <button type="button" aria-label="Help" className="grid size-[50px] place-items-center rounded-xl text-sidebar-foreground/60 hover:bg-sidebar-accent hover:text-white"><CircleHelp className="size-[19px]" /></button>
      </div>
    </aside>
  );
}

function ContextNavigation({ className, module, currentId, role, screens, query, setQuery, searchRef, onNavigate, onClose }: { className?: string; module: ModuleId; currentId: string; role: Role; screens: PrototypeScreen[]; query: string; setQuery: (value: string) => void; searchRef: React.RefObject<HTMLInputElement | null>; onNavigate: (id: string) => void; onClose?: () => void }) {
  const moduleInfo = modules.find((item) => item.id === module)!;
  const grouped = Object.entries(groupBy(screens, (item) => item.group));
  return (
    <aside className={`h-screen flex-col border-r bg-card ${className ?? ''}`}>
      <div className="flex min-h-[88px] items-start border-b px-5 py-4">
        <div className="min-w-0"><p className="font-heading text-lg font-semibold">{moduleInfo.label}</p><p className="mt-0.5 text-xs leading-5 text-muted-foreground">{moduleInfo.description}</p></div>
        {onClose ? <button type="button" aria-label="Close" onClick={onClose} className="ml-auto grid size-8 place-items-center rounded-lg hover:bg-muted"><X className="size-4" /></button> : null}
      </div>
      <div className="p-3">
        <div className="relative">
          <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
          <input ref={searchRef} value={query} onChange={(event) => setQuery(event.target.value)} aria-label="Search all screens" placeholder="Find any screen" className="h-10 w-full rounded-xl border bg-background pl-9 pr-3 text-sm outline-none focus:border-primary focus:ring-3 focus:ring-ring/20" />
        </div>
        <p className="mt-2 px-1 text-[10px] text-muted-foreground">Ctrl K · searches all {prototypeScreens.length - 1} screens</p>
      </div>
      <nav className="min-h-0 flex-1 overflow-y-auto px-3 pb-5" aria-label={`${moduleInfo.label} workflows`}>
        {grouped.map(([group, items]) => (
          <div key={group} className="mb-5">
            <p className="mb-1.5 px-2 text-[10px] font-bold uppercase tracking-[0.13em] text-muted-foreground">{group}</p>
            <div className="space-y-0.5">
              {items.map((item) => {
                const visible = isVisibleToRole(item, role);
                const blocked = !item.available || !visible;
                return (
                  <button key={item.id} type="button" onClick={() => onNavigate(item.id)} className={`flex min-h-9 w-full items-center gap-2 rounded-lg px-2.5 text-left text-[13px] transition ${currentId === item.id ? 'bg-accent font-semibold text-accent-foreground' : 'text-foreground/72 hover:bg-muted hover:text-foreground'}`}>
                    <span className={`size-1.5 rounded-full ${currentId === item.id ? 'bg-primary' : 'bg-border'}`} />
                    <span className="min-w-0 flex-1 truncate">{item.label}</span>
                    {blocked ? <LockKeyhole className="size-3 text-muted-foreground" /> : null}
                  </button>
                );
              })}
            </div>
          </div>
        ))}
      </nav>
      <div className="border-t bg-muted/35 px-4 py-3 text-xs text-muted-foreground"><span className="font-semibold text-success">244 / 244</span> active functions mapped</div>
    </aside>
  );
}

function TopBar({ role, setRole, state, setState, onMenu }: { role: Role; setRole: (value: Role) => void; state: DemoState; setState: (value: DemoState) => void; onMenu: () => void }) {
  return (
    <header className="sticky top-0 z-20 flex min-h-16 items-center gap-2 border-b bg-background/94 px-4 backdrop-blur md:px-6">
      <Button variant="outline" size="icon" aria-label="Open workspace navigation" onClick={onMenu} className="xl:hidden"><Menu className="size-4" /></Button>
      <div className="hidden items-center gap-2 text-xs text-muted-foreground sm:flex"><Store className="size-4" /><span className="font-medium text-foreground">Titan World · HEMW</span><ChevronDown className="size-3.5" /></div>
      <span aria-hidden="true" className="mx-1 hidden h-5 w-px bg-border sm:block" />
      <div className="hidden items-center gap-2 text-xs text-muted-foreground md:flex"><CalendarDays className="size-4" /><span>29 Aug 2026</span></div>
      <div className="ml-auto flex items-center gap-2">
        <label className="hidden items-center gap-2 rounded-lg border bg-card px-2.5 py-1.5 text-xs shadow-sm lg:flex">
          <Sparkles className="size-3.5 text-primary" /><span className="text-muted-foreground">State</span>
          <select aria-label="Demo screen state" value={state} onChange={(event) => setState(event.target.value as DemoState)} className="bg-transparent font-semibold outline-none">{demoStates.map((item) => <option key={item}>{item}</option>)}</select>
        </label>
        <label className="flex items-center gap-2 rounded-lg border bg-card px-2.5 py-1.5 text-xs shadow-sm">
          <UserRound className="size-3.5 text-muted-foreground" />
          <select aria-label="Demo user role" value={role} onChange={(event) => setRole(event.target.value as Role)} className="max-w-28 bg-transparent font-semibold outline-none sm:max-w-none">{roles.map((item) => <option key={item}>{item}</option>)}</select>
        </label>
        <Button variant="outline" size="icon" aria-label="More options"><MoreHorizontal className="size-4" /></Button>
      </div>
    </header>
  );
}

function ScreenHeader({ screen, role, onNavigate }: { screen: PrototypeScreen; role: Role; onNavigate: (id: string) => void }) {
  const moduleInfo = modules.find((item) => item.id === screen.module)!;
  return (
    <div className="mb-5 flex flex-wrap items-end justify-between gap-4">
      <div className="min-w-0">
        <div className="mb-2 flex flex-wrap items-center gap-2 text-[11px] font-semibold uppercase tracking-[0.11em] text-muted-foreground"><span>{moduleInfo.label}</span><span>/</span><span>{screen.group}</span>{!screen.available ? <Badge variant="outline" className="border-warning/35 bg-warning/10 text-warning">Unavailable by policy</Badge> : null}</div>
        <h1 className="font-heading text-2xl font-semibold tracking-[-0.02em] md:text-3xl">{screen.label}</h1>
        <p className="mt-1.5 max-w-3xl text-sm leading-6 text-muted-foreground">{screen.description}</p>
      </div>
      <div className="flex items-center gap-2">
        {screen.kind === 'report' ? <><Button variant="outline" className="h-10 gap-2"><Download className="size-4" /> Export</Button><Button className="h-10 gap-2"><RefreshCw className="size-4" /> Run report</Button></> : null}
        {screen.id === 'dashboard' ? <Button className="h-10 gap-2" onClick={() => onNavigate('manual-entry')}>Continue daily close <ArrowRight className="size-4" /></Button> : null}
        {!isVisibleToRole(screen, role) ? <Badge variant="destructive">Requires {screen.minimumRole}</Badge> : null}
      </div>
    </div>
  );
}

function ScreenState({ screen, role, state, onNavigate, onRow }: { screen: PrototypeScreen; role: Role; state: DemoState; onNavigate: (id: string) => void; onRow: (row: Row) => void }) {
  if (!screen.available) return <UnavailableState title={screen.label} reason={screen.unavailableReason ?? 'This function is unavailable.'} />;
  if (!isVisibleToRole(screen, role)) return <UnavailableState title="This role cannot perform this task" reason={`${screen.label} requires ${screen.minimumRole} access. Switch the demo role to preview it without changing production permissions.`} />;
  if (state !== 'Ready') return <AlternativeState state={state} screen={screen} />;
  if (screen.id === 'dashboard') return <TodayOverview onNavigate={onNavigate} />;
  if (screen.id === 'reports-home') return <ReportsOverview onNavigate={onNavigate} />;
  if (screen.kind === 'report') return <ReportSurface screen={screen} onRow={onRow} />;
  if (screen.kind === 'workflow') return <WorkflowSurface screen={screen} onNavigate={onNavigate} />;
  if (screen.kind === 'form') return <FormSurface screen={screen} />;
  if (screen.kind === 'settings') return <SettingsSurface screen={screen} />;
  if (screen.kind === 'coverage') return <CoverageSurface onNavigate={onNavigate} />;
  if (screen.kind === 'overview') return <ModuleOverview screen={screen} onNavigate={onNavigate} />;
  return <TableSurface screen={screen} onRow={onRow} />;
}

function TodayOverview({ onNavigate }: { onNavigate: (id: string) => void }) {
  return (
    <div className="space-y-4">
      <div className="grid gap-4 xl:grid-cols-[minmax(0,1.55fr)_minmax(330px,.75fr)]">
        <Card className="border-0 shadow-[0_1px_2px_rgb(15_23_42/4%),0_12px_30px_rgb(15_23_42/5%)] ring-1 ring-border">
          <CardHeader className="border-b"><CardTitle className="text-lg">Daily close · 29 August</CardTitle><CardDescription>Complete one missing input, review controls, then create the pack.</CardDescription><CardAction><Badge variant="outline" className="border-warning/30 bg-warning/10 text-warning">3 actions</Badge></CardAction></CardHeader>
          <CardContent className="grid gap-3 pt-1 sm:grid-cols-2">
            {readiness.map((item) => {
              const ready = item.state === 'Ready' || item.state === 'Passed';
              const action = item.state === 'Action';
              return <button key={item.label} type="button" aria-label={`${item.label}: ${item.state}. ${item.detail}`} onClick={() => onNavigate(item.target)} className="group flex min-h-[92px] items-start gap-3 rounded-xl border bg-card p-4 text-left transition hover:-translate-y-0.5 hover:border-primary/35 hover:shadow-md focus-visible:outline-none focus-visible:ring-3 focus-visible:ring-ring/30"><span className={`grid size-9 shrink-0 place-items-center rounded-lg ${ready ? 'bg-success/10 text-success' : action ? 'bg-warning/12 text-warning' : 'bg-muted text-muted-foreground'}`}><item.icon className="size-[18px]" /></span><span className="min-w-0 flex-1"><span className="flex items-center justify-between gap-2"><span className="font-semibold">{item.label}</span><span className={`text-xs font-semibold ${ready ? 'text-success' : action ? 'text-warning' : 'text-muted-foreground'}`}>{item.state}</span></span><span className="mt-1 block text-xs leading-5 text-muted-foreground">{item.detail}</span></span></button>;
            })}
          </CardContent>
        </Card>
        <Card className="border-0 bg-ink text-white shadow-[0_16px_38px_rgb(15_29_48/18%)] ring-0"><CardHeader><CardTitle className="text-lg text-white">Today at a glance</CardTitle><CardDescription className="text-white/55">Combined · Titan + Helios</CardDescription></CardHeader><CardContent><p className="font-heading text-3xl font-semibold tracking-tight">₹8,42,680</p><div className="mt-1 flex items-center gap-2 text-xs text-white/60"><span>Net sales</span><span>·</span><span className="text-mint">+8.4% vs LY</span></div><div className="mt-6 grid grid-cols-3 gap-3 border-t border-white/10 pt-5">{[['Invoices','42'],['Items','58'],['Returns','₹12.4k']].map(([label,value]) => <div key={label}><p className="text-lg font-semibold">{value}</p><p className="mt-0.5 text-[11px] text-white/50">{label}</p></div>)}</div><Button variant="secondary" onClick={() => onNavigate('report-dsr')} className="mt-6 h-10 w-full justify-between bg-white/10 text-white hover:bg-white/15">Open daily sales report <ArrowRight className="size-4" /></Button></CardContent></Card>
      </div>
      <div className="grid gap-4 lg:grid-cols-[minmax(0,1.25fr)_minmax(300px,.75fr)]">
        <Card className="border-0 shadow-sm ring-1 ring-border"><CardHeader><CardTitle>Reporting readiness</CardTitle><CardDescription>Source completeness across both stores.</CardDescription><CardAction><span className="text-sm font-semibold text-success">83%</span></CardAction></CardHeader><CardContent><Progress value={83} className="[&_[data-slot=progress-track]]:h-2 [&_[data-slot=progress-indicator]]:bg-success" /><div className="mt-5 grid gap-2 sm:grid-cols-3">{[['HEMW','6 / 6','Complete'],['WLMHW','5 / 6','Walk-ins required'],['Controls','4 / 4','Passed']].map(([store,count,status]) => <div key={store} className="rounded-xl bg-muted/65 p-3"><p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">{store}</p><p className="mt-1.5 text-lg font-semibold">{count}</p><p className="text-xs text-muted-foreground">{status}</p></div>)}</div></CardContent></Card>
        <Card className="border-0 shadow-sm ring-1 ring-border"><CardHeader><CardTitle>System status</CardTitle><CardDescription>Operational services are healthy.</CardDescription></CardHeader><CardContent className="space-y-3">{[['SQL database','Connected',Database],['Last backup','Today · 02:00',CheckCircle2],['Import watch','Monitoring',ShieldCheck]].map(([label,value,Icon]) => { const StatusIcon = Icon as typeof Database; return <div key={label as string} className="flex items-center gap-3"><span className="grid size-8 place-items-center rounded-lg bg-success/10 text-success"><StatusIcon className="size-4" /></span><div><p className="text-sm font-medium">{label as string}</p><p className="text-xs text-muted-foreground">{value as string}</p></div><CheckCircle2 className="ml-auto size-4 text-success" /></div>; })}</CardContent></Card>
      </div>
    </div>
  );
}

function ReportsOverview({ onNavigate }: { onNavigate: (id: string) => void }) {
  const reports = prototypeScreens.filter((item) => reportCodes.has(item.id));
  const grouped = Object.entries(groupBy(reports, (item) => item.group));
  return (
    <div className="space-y-5">
      <div className="grid gap-3 md:grid-cols-3">{[['Favourite','Daily Sales / DSR','report-dsr'],['Recently opened','Closing Stock','report-stock-closing'],['Needs attention','Tender Reconciliation','report-tender']].map(([eyebrow,title,target], index) => <button key={title} type="button" onClick={() => onNavigate(target)} className="rounded-xl border bg-card p-4 text-left shadow-sm transition hover:border-primary/35 hover:shadow-md"><p className="text-[10px] font-bold uppercase tracking-[0.13em] text-muted-foreground">{eyebrow}</p><div className="mt-3 flex items-center gap-3"><span className={`grid size-9 place-items-center rounded-lg ${index === 2 ? 'bg-warning/10 text-warning' : 'bg-primary/10 text-primary'}`}><BarChart3 className="size-[18px]" /></span><span className="font-semibold">{title}</span><ArrowRight className="ml-auto size-4 text-muted-foreground" /></div></button>)}</div>
      {grouped.map(([group, items]) => <Card key={group} className="border-0 shadow-sm ring-1 ring-border"><CardHeader><CardTitle>{group}</CardTitle><CardDescription>{items.length} governed {items.length === 1 ? 'report' : 'reports'} with shared filters and export actions.</CardDescription></CardHeader><CardContent className="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">{items.map((item) => <button key={item.id} type="button" onClick={() => onNavigate(item.id)} className="flex min-h-12 items-center gap-3 rounded-xl border px-3 text-left text-sm font-medium transition hover:border-primary/35 hover:bg-accent/45"><FileSpreadsheet className="size-4 text-primary" /><span className="flex-1">{item.label}</span><ArrowRight className="size-3.5 text-muted-foreground" /></button>)}</CardContent></Card>)}
    </div>
  );
}

function ReportSurface({ screen, onRow }: { screen: PrototypeScreen; onRow: (row: Row) => void }) {
  return (
    <div className="space-y-4">
      <FilterBar />
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">{[['Net value','₹8,42,680','+8.4% vs LY'],['Quantity','58','+5 units'],['Invoices','42','2 returns'],['Control status','Passed','R022 ↔ R025']].map(([label,value,detail], index) => <Card key={label} size="sm" className="border-0 shadow-sm ring-1 ring-border"><CardHeader><CardDescription>{label}</CardDescription><CardAction>{index === 3 ? <CheckCircle2 className="size-4 text-success" /> : null}</CardAction><CardTitle className="text-xl">{value}</CardTitle></CardHeader><CardContent className={`text-xs ${index === 3 ? 'text-success' : 'text-muted-foreground'}`}>{detail}</CardContent></Card>)}</div>
      <Card className="border-0 shadow-sm ring-1 ring-border"><CardHeader><CardTitle>{screen.label} detail</CardTitle><CardDescription>Dummy data · open any row to inspect source lineage without leaving this report.</CardDescription><CardAction><Button variant="outline" size="sm" className="gap-1.5"><ListFilter className="size-3.5" /> Columns</Button></CardAction></CardHeader><CardContent className="overflow-x-auto px-0"><DataTable rows={reportRows} onRow={onRow} /></CardContent></Card>
    </div>
  );
}

function FilterBar() {
  return <Card className="border-0 shadow-sm ring-1 ring-border"><CardContent className="flex flex-wrap items-end gap-3 pt-0"><label className="min-w-40 flex-1 text-xs font-semibold text-muted-foreground">Period<select className="mt-1.5 h-10 w-full rounded-lg border bg-card px-3 text-sm font-medium text-foreground outline-none"><option>FTD · 29 Aug 2026</option><option>MTD · August 2026</option><option>YTD · FY 2026–27</option></select></label><label className="min-w-36 flex-1 text-xs font-semibold text-muted-foreground">Store<select className="mt-1.5 h-10 w-full rounded-lg border bg-card px-3 text-sm font-medium text-foreground outline-none"><option>Combined stores</option><option>HEMW</option><option>WLMHW</option></select></label><label className="min-w-36 flex-1 text-xs font-semibold text-muted-foreground">Segment<select className="mt-1.5 h-10 w-full rounded-lg border bg-card px-3 text-sm font-medium text-foreground outline-none"><option>All segments</option><option>GAUTO</option><option>HSMART</option></select></label><Button className="h-10 gap-2 px-4"><RefreshCw className="size-4" /> Apply</Button></CardContent></Card>;
}

function WorkflowSurface({ screen, onNavigate }: { screen: PrototypeScreen; onNavigate: (id: string) => void }) {
  const importFlow = screen.module === 'imports';
  const accountingFlow = screen.module === 'accounting';
  const steps = importFlow ? ['Select source', 'Verify profile', 'Review controls', 'Commit facts'] : accountingFlow ? ['Prepare batch', 'Review mapping', 'Validate balance', 'Export XML'] : ['Check sources', 'Complete inputs', 'Review controls', 'Finalise'];
  return <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_360px]"><Card className="border-0 shadow-sm ring-1 ring-border"><CardHeader><CardTitle>{screen.label}</CardTitle><CardDescription>A guided workflow keeps context, constraints and the next action together.</CardDescription></CardHeader><CardContent><ol className="space-y-3">{steps.map((step,index) => <li key={step} className={`flex items-start gap-3 rounded-xl border p-4 ${index === 1 ? 'border-primary/35 bg-accent/35' : ''}`}><span className={`grid size-8 shrink-0 place-items-center rounded-full text-xs font-bold ${index === 0 ? 'bg-success text-white' : index === 1 ? 'bg-primary text-white' : 'bg-muted text-muted-foreground'}`}>{index === 0 ? <Check className="size-4" /> : index + 1}</span><div className="min-w-0 flex-1"><p className="font-semibold">{step}</p><p className="mt-0.5 text-xs text-muted-foreground">{index === 0 ? 'Completed with audit evidence' : index === 1 ? 'Current step · one action required' : 'Available after the prior step'}</p></div>{index === 1 ? <Button size="sm">Continue</Button> : null}</li>)}</ol></CardContent></Card><Card className="h-fit border-0 shadow-sm ring-1 ring-border"><CardHeader><CardTitle>Before you continue</CardTitle><CardDescription>Business rules stay visible at the moment of action.</CardDescription></CardHeader><CardContent className="space-y-3 text-sm"><div className="rounded-xl bg-success/8 p-3 text-success"><p className="font-semibold">Controls available</p><p className="mt-1 text-xs leading-5 opacity-80">The latest source and audit evidence will be attached automatically.</p></div><div className="rounded-xl bg-muted p-3"><p className="font-semibold">Missing is not zero</p><p className="mt-1 text-xs leading-5 text-muted-foreground">Unavailable input remains explicit and can block finalisation.</p></div><Button variant="outline" className="w-full justify-between" onClick={() => onNavigate('control-summary')}>View control summary <ArrowRight className="size-4" /></Button></CardContent></Card></div>;
}

function FormSurface({ screen }: { screen: PrototypeScreen }) {
  const register = screen.id.startsWith('register-');
  return <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_360px]"><Card className="border-0 shadow-sm ring-1 ring-border"><CardHeader><CardTitle>{screen.label}</CardTitle><CardDescription>{register ? 'Record the document once; linkage, access and audit history are preserved.' : 'Enter only values that are not supplied by governed ETP reports.'}</CardDescription></CardHeader><CardContent className="grid gap-4 sm:grid-cols-2"><Field label={register ? 'Reference number' : 'Business date'} value={register ? 'INW-2026-0842' : '29 Aug 2026'} /><Field label={register ? 'Party / source' : 'Store'} value={register ? 'Titan Company Limited' : 'WLMHW'} /><Field label={register ? 'Document date' : 'Walk-ins'} value={register ? '29 Aug 2026' : '126'} /><Field label={register ? 'Amount' : 'Reason'} value={register ? '₹1,28,450' : 'Verified door counter reading'} /><label className="sm:col-span-2 text-xs font-semibold text-muted-foreground">Notes<textarea className="mt-1.5 min-h-24 w-full rounded-xl border bg-card p-3 text-sm font-normal text-foreground outline-none focus:border-primary focus:ring-3 focus:ring-ring/20" defaultValue="Reviewed against the supporting source document." /></label><div className="sm:col-span-2 flex justify-end gap-2 border-t pt-4"><Button variant="outline">Cancel</Button><Button className="gap-2"><Check className="size-4" /> Save with audit reason</Button></div></CardContent></Card><Card className="h-fit border-0 shadow-sm ring-1 ring-border"><CardHeader><CardTitle>Audit preview</CardTitle><CardDescription>This is recorded with your Windows identity.</CardDescription></CardHeader><CardContent className="space-y-3 text-sm">{[['User','SAGAR\\sagar'],['Role','Store Manager'],['Store','WLMHW'],['Business date','29 Aug 2026']].map(([label,value]) => <div key={label} className="flex justify-between gap-4"><span className="text-muted-foreground">{label}</span><span className="font-medium">{value}</span></div>)}<div className="rounded-xl bg-warning/10 p-3 text-xs leading-5 text-warning">Locked days require an approved adjustment request instead of direct editing.</div></CardContent></Card></div>;
}

function Field({ label, value }: { label: string; value: string }) { return <label className="text-xs font-semibold text-muted-foreground">{label}<input defaultValue={value} className="mt-1.5 h-10 w-full rounded-lg border bg-card px-3 text-sm font-normal text-foreground outline-none focus:border-primary focus:ring-3 focus:ring-ring/20" /></label>; }

function TableSurface({ screen, onRow }: { screen: PrototypeScreen; onRow: (row: Row) => void }) {
  const exception = screen.module === 'exceptions' || screen.id.includes('failure') || screen.id.includes('conflict');
  const rows = exception ? issueRows : reportRows;
  return <div className="space-y-4"><div className="flex flex-wrap gap-2"><Button className="gap-2"><Upload className="size-4" /> {screen.module === 'imports' ? 'Add source' : 'New record'}</Button><Button variant="outline" className="gap-2"><ListFilter className="size-4" /> Filters</Button><Button variant="outline" className="gap-2"><Download className="size-4" /> Export view</Button></div><Card className="border-0 shadow-sm ring-1 ring-border"><CardHeader><CardTitle>{screen.label}</CardTitle><CardDescription>{rows.length} representative records · dummy data with safe, consistent identifiers.</CardDescription></CardHeader><CardContent className="overflow-x-auto px-0"><DataTable rows={rows} onRow={onRow} /></CardContent></Card></div>;
}

function DataTable({ rows, onRow }: { rows: readonly Row[]; onRow: (row: Row) => void }) {
  return <table className="w-full min-w-[760px] text-left text-sm"><thead><tr className="border-y bg-muted/55 text-[11px] uppercase tracking-[0.08em] text-muted-foreground"><th className="px-4 py-3 font-semibold">Reference</th><th className="px-4 py-3 font-semibold">Description</th><th className="px-4 py-3 font-semibold">Store</th><th className="px-4 py-3 font-semibold">Quantity</th><th className="px-4 py-3 font-semibold">Value / area</th><th className="px-4 py-3 font-semibold">Status</th><th aria-label="Row actions" className="px-4 py-3" /></tr></thead><tbody>{rows.map((row) => <tr key={row.reference} className="border-b transition hover:bg-accent/25"><td className="px-4 py-3 font-medium">{row.reference}</td><td className="max-w-xs px-4 py-3 text-muted-foreground">{row.description}</td><td className="px-4 py-3">{row.store}</td><td className="px-4 py-3 tabular-nums">{row.quantity}</td><td className="px-4 py-3 font-medium tabular-nums">{row.value}</td><td className="px-4 py-3"><StatusBadge status={row.status} /></td><td className="px-4 py-3"><Button variant="ghost" size="sm" onClick={() => onRow(row)} className="gap-1">Details <PanelRightOpen className="size-3.5" /></Button></td></tr>)}</tbody></table>;
}

function StatusBadge({ status }: { status: string }) { const warning = status === 'Action' || status === 'Review'; return <Badge variant="outline" className={warning ? 'border-warning/30 bg-warning/10 text-warning' : 'border-success/30 bg-success/10 text-success'}>{status}</Badge>; }

function SettingsSurface({ screen }: { screen: PrototypeScreen }) {
  return <div className="grid gap-4 lg:grid-cols-2"><Card className="border-0 shadow-sm ring-1 ring-border"><CardHeader><CardTitle>{screen.label}</CardTitle><CardDescription>Changes are validated before they become active.</CardDescription></CardHeader><CardContent className="space-y-4"><Field label="Configuration name" value="ETP Reporting · Production" /><Field label="Default store" value="HEMW" /><label className="flex items-center justify-between rounded-xl border p-3"><span><span className="block text-sm font-semibold">Enable governed automation</span><span className="text-xs text-muted-foreground">Requires a healthy database and approved folder.</span></span><input type="checkbox" aria-label="Enable governed automation" defaultChecked className="size-4 accent-primary" /></label><div className="flex justify-end gap-2 border-t pt-4"><Button variant="outline">Discard</Button><Button>Validate and save</Button></div></CardContent></Card><Card className="h-fit border-0 shadow-sm ring-1 ring-border"><CardHeader><CardTitle>Impact preview</CardTitle><CardDescription>Know what changes before saving.</CardDescription></CardHeader><CardContent className="space-y-3">{[['Affected stores','2'],['Dependent workflows','4'],['Pending approval','No'],['Last changed','28 Aug · Owner']].map(([label,value]) => <div key={label} className="flex items-center justify-between rounded-lg bg-muted/60 px-3 py-2 text-sm"><span className="text-muted-foreground">{label}</span><span className="font-semibold">{value}</span></div>)}</CardContent></Card></div>;
}

function ModuleOverview({ screen, onNavigate }: { screen: PrototypeScreen; onNavigate: (id: string) => void }) {
  const items = prototypeScreens.filter((item) => item.module === screen.module && item.id !== screen.id && item.available).slice(0, 6);
  return <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_340px]"><Card className="border-0 shadow-sm ring-1 ring-border"><CardHeader><CardTitle>Continue your work</CardTitle><CardDescription>Common tasks are ordered by operational sequence, not implementation area.</CardDescription></CardHeader><CardContent className="grid gap-2 sm:grid-cols-2">{items.map((item,index) => <button key={item.id} onClick={() => onNavigate(item.id)} className="flex min-h-20 items-center gap-3 rounded-xl border p-3 text-left transition hover:border-primary/35 hover:shadow-sm"><span className={`grid size-9 place-items-center rounded-lg ${index === 0 ? 'bg-primary text-white' : 'bg-muted text-muted-foreground'}`}>{index + 1}</span><span><span className="block font-semibold">{item.label}</span><span className="mt-0.5 block text-xs text-muted-foreground">{item.group}</span></span><ArrowRight className="ml-auto size-4 text-muted-foreground" /></button>)}</CardContent></Card><Card className="h-fit border-0 shadow-sm ring-1 ring-border"><CardHeader><CardTitle>Workspace health</CardTitle><CardDescription>No blocking system issues.</CardDescription></CardHeader><CardContent className="space-y-3">{[['Ready','8 items',CheckCircle2],['Needs review','2 items',Clock3],['Blocked','0 items',ShieldAlert]].map(([label,value,Icon]) => { const StatusIcon = Icon as typeof Clock3; return <div key={label as string} className="flex items-center gap-3 rounded-xl bg-muted/55 p-3"><StatusIcon className="size-4 text-primary" /><span className="text-sm font-medium">{label as string}</span><span className="ml-auto text-xs text-muted-foreground">{value as string}</span></div>; })}</CardContent></Card></div>;
}

function CoverageSurface({ onNavigate }: { onNavigate: (id: string) => void }) {
  const entries = coverage.entries.map((entry) => ({ ...entry, screen: screenForFunction(entry.id) }));
  const active = entries.filter((entry) => entry.disposition !== 'DEFERRED_UNAVAILABLE');
  const deferred = entries.filter((entry) => entry.disposition === 'DEFERRED_UNAVAILABLE');
  const allMapped = active.every((entry) => prototypeScreens.some((screen) => screen.id === entry.screen));
  return <div className="space-y-4"><div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">{[[active.length,'Active functions','Mapped to prototype'],[prototypeScreens.length - 1,'Screens & states','Clickable'],[coverage.reportCount,'Production reports','With dummy data'],[deferred.length,'Deferred functions','Shown with reason']].map(([value,label,detail]) => <Card key={label as string} size="sm" className="border-0 shadow-sm ring-1 ring-border"><CardHeader><CardDescription>{label as string}</CardDescription><CardTitle className="text-2xl">{value as number}</CardTitle></CardHeader><CardContent className="text-xs text-muted-foreground">{detail as string}</CardContent></Card>)}</div><Card className="border-0 shadow-sm ring-1 ring-border"><CardHeader><CardTitle>Coverage result</CardTitle><CardDescription>Every active function resolves to a valid prototype destination; deferred functions remain visible and explain why they are unavailable.</CardDescription><CardAction><Badge variant="outline" className={allMapped ? 'border-success/30 bg-success/10 text-success' : 'border-critical/30 bg-critical/10 text-critical'}>{allMapped ? 'PASS · 244 / 244' : 'CHECK REQUIRED'}</Badge></CardAction></CardHeader><CardContent className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">{Object.entries(groupBy(active, (entry) => entry.kind)).map(([kind, items]) => <div key={kind} className="rounded-xl bg-muted/55 p-3"><p className="text-xs font-semibold text-muted-foreground">{kind}</p><p className="mt-1 text-xl font-semibold">{items.length}</p><p className="text-xs text-success">Mapped</p></div>)}</CardContent></Card><Card className="border-0 shadow-sm ring-1 ring-border"><CardHeader><CardTitle>Audit findings addressed</CardTitle><CardDescription>The prototype changes are traceable to observed friction in the current WPF experience.</CardDescription></CardHeader><CardContent className="grid gap-2 lg:grid-cols-2">{auditFindings.map((finding) => <div key={finding.title} className="rounded-xl border p-3"><div className="flex items-start gap-3"><Badge variant="outline" className={finding.priority === 'P1' ? 'border-critical/30 bg-critical/8 text-critical' : 'border-warning/30 bg-warning/8 text-warning'}>{finding.priority}</Badge><div><p className="text-sm font-semibold">{finding.title}</p><p className="mt-1 text-xs leading-5 text-muted-foreground">{finding.change}</p></div></div></div>)}</CardContent></Card><Card className="border-0 shadow-sm ring-1 ring-border"><CardHeader><CardTitle>Deferred and unavailable</CardTitle><CardDescription>These are demonstrated as safe locked states and are not fabricated.</CardDescription></CardHeader><CardContent className="space-y-2">{deferred.map((entry) => <button key={entry.id} type="button" onClick={() => onNavigate(entry.screen)} className="flex w-full items-center gap-3 rounded-xl border p-3 text-left hover:border-warning/45"><LockKeyhole className="size-4 text-warning" /><span className="min-w-0 flex-1"><span className="block text-sm font-semibold">{entry.name}</span><span className="block truncate text-xs text-muted-foreground">{entry.note}</span></span><ArrowRight className="size-4 text-muted-foreground" /></button>)}</CardContent></Card></div>;
}

function AlternativeState({ state, screen }: { state: DemoState; screen: PrototypeScreen }) {
  const content = {
    Loading: { icon: RefreshCw, title: `Loading ${screen.label.toLowerCase()}`, body: 'The previous context stays visible while fresh data is checked.', tone: 'text-primary bg-primary/10' },
    Empty: { icon: FolderArchive, title: 'No records for these filters', body: 'Try another period or store. Nothing has been replaced with zero.', tone: 'text-muted-foreground bg-muted' },
    Error: { icon: ShieldAlert, title: 'This view could not be refreshed', body: 'Your previous result is safe. Retry, or open support details without losing filters.', tone: 'text-critical bg-critical/10' },
    Locked: { icon: LockKeyhole, title: 'Business day is locked', body: 'Viewing is allowed. Changes require an approved adjustment request with a reason.', tone: 'text-warning bg-warning/10' },
  }[state as Exclude<DemoState, 'Ready'>];
  const Icon = content.icon;
  return <Card className="border-0 shadow-sm ring-1 ring-border"><CardContent className="grid min-h-[390px] place-items-center"><div className="max-w-md text-center"><span className={`mx-auto grid size-12 place-items-center rounded-xl ${content.tone}`}><Icon className={`size-5 ${state === 'Loading' ? 'animate-spin' : ''}`} /></span><h2 className="mt-4 text-lg font-semibold">{content.title}</h2><p className="mt-1.5 text-sm leading-6 text-muted-foreground">{content.body}</p><div className="mt-5 flex justify-center gap-2"><Button variant="outline">View details</Button><Button>{state === 'Error' ? 'Retry' : state === 'Locked' ? 'Request adjustment' : 'Change filters'}</Button></div></div></CardContent></Card>;
}

function UnavailableState({ title, reason }: { title: string; reason: string }) { return <Card className="border-0 shadow-sm ring-1 ring-border"><CardContent className="grid min-h-[390px] place-items-center"><div className="max-w-lg text-center"><span className="mx-auto grid size-12 place-items-center rounded-xl bg-warning/10 text-warning"><LockKeyhole className="size-5" /></span><h2 className="mt-4 text-lg font-semibold">{title}</h2><p className="mt-1.5 text-sm leading-6 text-muted-foreground">{reason}</p><Badge variant="outline" className="mt-5 border-warning/30 bg-warning/10 text-warning">Not fabricated · policy-safe preview</Badge></div></CardContent></Card>; }

function DetailDrawer({ row, onClose }: { row: Row; onClose: () => void }) {
  return <dialog open className="fixed inset-0 z-50 m-0 flex h-screen w-screen max-w-none justify-end border-0 bg-ink/25 p-0" aria-label={`Details for ${row.reference}`}><button className="flex-1" onClick={onClose} aria-label="Close details" /><aside className="h-full w-[min(470px,94vw)] overflow-y-auto border-l bg-card p-5 shadow-2xl"><div className="flex items-start gap-3"><span className="grid size-10 place-items-center rounded-xl bg-primary/10 text-primary"><FileText className="size-5" /></span><div><p className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">Source detail</p><h2 className="mt-0.5 text-xl font-semibold">{row.reference}</h2></div><Button variant="ghost" size="icon" aria-label="Close source detail" onClick={onClose} className="ml-auto"><X className="size-4" /></Button></div><div className="mt-6 space-y-3">{Object.entries(row).map(([label,value]) => <div key={label} className="rounded-xl border p-3"><p className="text-[10px] font-bold uppercase tracking-wider text-muted-foreground">{label}</p><p className="mt-1 text-sm font-medium">{value}</p></div>)}</div><Card className="mt-5 border-0 bg-muted/60 ring-0"><CardHeader><CardTitle>Lineage</CardTitle><CardDescription>Every displayed value can be traced to an accepted source.</CardDescription></CardHeader><CardContent className="space-y-3">{['ETP source accepted','Canonical facts persisted','Report control passed'].map((item,index) => <div key={item} className="flex items-center gap-3"><span className="grid size-7 place-items-center rounded-full bg-success text-white"><Check className="size-3.5" /></span><div><p className="text-sm font-medium">{item}</p><p className="text-xs text-muted-foreground">Step {index + 1} · evidence available</p></div></div>)}</CardContent></Card><Button className="mt-5 w-full gap-2"><FileText className="size-4" /> Open source evidence</Button></aside></dialog>;
}
