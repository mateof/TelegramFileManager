import { Button, cn, confirm, Input, Skeleton, useMediaQuery } from '@tfm/ui';
import { ArrowLeft, ChevronRight, RefreshCw, Save, Search, Settings, Undo2, X } from 'lucide-react';
import { useEffect, useMemo, useState, type ComponentType } from 'react';
import { Link, Navigate, useBlocker, useNavigate, useParams } from 'react-router';
import { t } from '@/i18n';
import { errorMessage } from '@/lib/api';
import { PageHeader } from '@/shell/PageHeader';
import { anchorOf, isSectionId, searchSettings, sectionGroups, sections, type SectionDef, type SectionId } from '../catalog';
import { useSettingsForm, type SettingsForm } from '../model/useSettingsForm';
import { AboutSection } from '../sections/AboutSection';
import { AppearanceSection } from '../sections/AppearanceSection';
import { AppsSection } from '../sections/AppsSection';
import { ChannelsSection } from '../sections/ChannelsSection';
import { LibrarySection } from '../sections/LibrarySection';
import { SecuritySection } from '../sections/SecuritySection';
import {
  ConnectionsSection,
  FileManagerSection,
  GeneralSection,
  NotificationsSection,
  StreamingSection,
  TasksSection,
  UploadsSection
} from '../sections/ServerSections';
import type { FormSectionProps } from '../sections/types';
import { WebDavSection } from '../sections/WebDavSection';

const FORM_SECTIONS: Partial<Record<SectionId, ComponentType<FormSectionProps>>> = {
  general: GeneralSection,
  connections: ConnectionsSection,
  streaming: StreamingSection,
  uploads: UploadsSection,
  'file-manager': FileManagerSection,
  notifications: NotificationsSection,
  channels: ChannelsSection,
  tasks: TasksSection,
  library: LibrarySection,
  apps: AppsSection,
  webdav: WebDavSection
};

const STANDALONE_SECTIONS: Partial<Record<SectionId, ComponentType>> = {
  security: SecuritySection,
  appearance: AppearanceSection,
  about: AboutSection
};

function SectionBody({ id, form }: { id: SectionId; form: SettingsForm }) {
  const Standalone = STANDALONE_SECTIONS[id];
  if (Standalone) return <Standalone />;
  const FormSection = FORM_SECTIONS[id];
  if (!FormSection) return null;
  if (form.config.isPending) {
    return (
      <div className="flex flex-col gap-4">
        <Skeleton className="h-40" />
        <Skeleton className="h-28" />
      </div>
    );
  }
  if (form.config.isError || !form.values) {
    return (
      <div className="flex flex-col items-start gap-3 rounded-2xl border border-danger/30 bg-danger-soft p-5 text-sm text-danger">
        {t('Could not load the settings')}: {errorMessage(form.config.error)}
        <Button size="sm" variant="secondary" icon={<RefreshCw />} onClick={() => void form.config.refetch()}>
          {t('Retry')}
        </Button>
      </div>
    );
  }
  return <FormSection form={form} values={form.values} />;
}

function SearchBox({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  return (
    <Input
      type="search"
      value={value}
      onChange={(e) => onChange(e.target.value)}
      placeholder={t('Search settings...')}
      aria-label={t('Search settings...')}
      leading={<Search />}
      trailing={
        value ? (
          <button type="button" onClick={() => onChange('')} aria-label={t('Clear search')} className="flex size-7 items-center justify-center rounded-md text-fg-subtle hover:text-fg">
            <X className="size-4" />
          </button>
        ) : undefined
      }
    />
  );
}

function SearchResults({ query, onPick }: { query: string; onPick: () => void }) {
  const secs = sections();
  const results = searchSettings(query);
  const q = query.trim().toLowerCase();
  const matchingSections = secs.filter((s) => `${s.title} ${s.description}`.toLowerCase().includes(q));
  if (results.length === 0 && matchingSections.length === 0) {
    return <p className="px-2 py-6 text-center text-sm text-fg-muted">{t('No settings match "{query}".', { query })}</p>;
  }
  return (
    <ul className="flex flex-col gap-0.5" aria-label={t('Search results')}>
      {matchingSections.map((s) => (
        <li key={`s-${s.id}`}>
          <Link to={`/settings/${s.id}`} onClick={onPick} className="flex items-center gap-2.5 rounded-lg px-2.5 py-2 text-sm text-fg hover:bg-surface-2">
            <s.icon className="size-4 text-accent-400" />
            <span className="font-medium">{s.title}</span>
          </Link>
        </li>
      ))}
      {results.map((r) => {
        const section = secs.find((s) => s.id === r.section);
        return (
          <li key={r.id}>
            <Link to={`/settings/${r.section}#${anchorOf(r.id)}`} onClick={onPick} className="flex flex-col rounded-lg px-2.5 py-2 hover:bg-surface-2">
              <span className="text-sm text-fg">{r.label}</span>
              <span className="text-[11px] text-fg-subtle">{section?.title}</span>
            </Link>
          </li>
        );
      })}
    </ul>
  );
}

function NavItem({ section: s, active, compact }: { section: SectionDef; active: boolean; compact?: boolean }) {
  return (
    <Link
      to={`/settings/${s.id}`}
      aria-current={active ? 'page' : undefined}
      className={cn(
        'group flex items-center gap-3 rounded-xl px-2.5 transition-colors duration-150 focus-visible:outline-2 focus-visible:outline-ring',
        compact ? 'py-2' : 'py-3',
        active ? 'bg-primary-soft text-fg' : 'text-fg-muted hover:bg-surface-2 hover:text-fg'
      )}
    >
      <span
        className={cn(
          'flex size-8 shrink-0 items-center justify-center rounded-lg transition-colors',
          active ? 'bg-primary text-on-accent shadow-glow' : 'bg-surface-2 text-fg-muted group-hover:text-accent-300'
        )}
      >
        <s.icon className="size-4" />
      </span>
      <span className="min-w-0 flex-1">
        <span className="block truncate text-sm font-medium">{s.title}</span>
        {!compact && <span className="block truncate text-xs text-fg-subtle">{s.description}</span>}
      </span>
      {!compact && <ChevronRight className="size-4 text-fg-subtle" />}
    </Link>
  );
}

function SectionNav({ active, compact }: { active?: SectionId; compact?: boolean }) {
  const secs = sections();
  return (
    <nav aria-label={t('Settings sections')} className="flex flex-col gap-4">
      {sectionGroups().map((g) => (
        <div key={g.id} className="flex flex-col gap-0.5">
          <p className="px-2.5 pb-1 text-[11px] font-semibold uppercase tracking-wider text-fg-subtle">{g.label}</p>
          {secs
            .filter((s) => s.group === g.id)
            .map((s) => (
              <NavItem key={s.id} section={s} active={s.id === active} compact={compact} />
            ))}
        </div>
      ))}
    </nav>
  );
}

function SaveBar({ form }: { form: SettingsForm }) {
  const invalid = Object.keys(form.errors).length;
  if (!form.isDirty && !form.saving) return null;
  return (
    <div className="sticky bottom-3 z-10 mt-2 animate-slide-in-bottom">
      <div className="glass-strong mx-auto flex max-w-3xl flex-wrap items-center gap-3 rounded-2xl border border-border px-4 py-3 shadow-elevated" role="region" aria-label={t('Unsaved changes')}>
        <span className="relative flex size-2.5">
          <span className="absolute inline-flex size-full animate-ping rounded-full bg-warning opacity-60" />
          <span className="relative inline-flex size-2.5 rounded-full bg-warning" />
        </span>
        <p className="min-w-0 flex-1 text-sm text-fg">
          {invalid > 0 ? t('Fix {count} invalid values to save.', { count: invalid }) : t('{count} unsaved changes', { count: form.dirty.length })}
        </p>
        <Button variant="ghost" size="sm" icon={<Undo2 />} onClick={form.discard} disabled={form.saving}>
          {t('Discard')}
        </Button>
        <Button size="sm" icon={<Save />} loading={form.saving} disabled={invalid > 0} onClick={() => void form.submit()}>
          {t('Save')}
        </Button>
      </div>
    </div>
  );
}

/** Asks before leaving the settings with unsaved changes (in-app navigation and tab close). */
function useDirtyGuard(form: SettingsForm) {
  const dirty = form.isDirty;
  const { discard } = form;
  const blocker = useBlocker(
    ({ currentLocation, nextLocation }) => dirty && currentLocation.pathname !== nextLocation.pathname && !nextLocation.pathname.startsWith('/settings')
  );
  useEffect(() => {
    if (blocker.state !== 'blocked') return;
    void confirm({
      title: t('Discard the unsaved changes?'),
      description: t('You changed settings that were not saved.'),
      confirmLabel: t('Discard'),
      destructive: true
    }).then((ok) => {
      if (ok) {
        discard();
        blocker.proceed();
      } else blocker.reset();
    });
  }, [blocker, discard]);
  useEffect(() => {
    if (!dirty) return;
    const onBeforeUnload = (e: BeforeUnloadEvent) => e.preventDefault();
    window.addEventListener('beforeunload', onBeforeUnload);
    return () => window.removeEventListener('beforeunload', onBeforeUnload);
  }, [dirty]);
}

/** `/settings` and `/settings/:section`: left navigation on desktop, list → section on mobile. */
export default function SettingsPage() {
  const params = useParams();
  const navigate = useNavigate();
  const desktop = useMediaQuery('(min-width: 1024px)');
  const form = useSettingsForm();
  const [query, setQuery] = useState('');
  useDirtyGuard(form);

  const { submit } = form;
  // Ctrl/Cmd+S saves
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') {
        e.preventDefault();
        void submit();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [submit]);

  const raw = params['section'];
  const section: SectionId | undefined = isSectionId(raw) ? raw : undefined;
  const current = useMemo(() => sections().find((s) => s.id === (section ?? 'general')), [section]);
  if (raw && !section) return <Navigate to="/settings" replace />;

  const searching = query.trim().length > 0;
  const showList = !desktop && !section;
  const mobileSection = !desktop && !!section;

  return (
    <div className="mx-auto flex w-full max-w-6xl flex-col gap-6 p-4 sm:p-6 lg:p-8">
      <PageHeader
        title={mobileSection && current ? current.title : t('Settings')}
        documentTitle={current && section ? `${current.title} · ${t('Settings')}` : t('Settings')}
        description={mobileSection ? current?.description : t('Server, Telegram, downloads, appearance and security.')}
        icon={mobileSection && current ? <current.icon /> : <Settings />}
        actions={
          mobileSection ? (
            <Button variant="ghost" size="sm" icon={<ArrowLeft />} onClick={() => navigate('/settings')}>
              {t('All settings')}
            </Button>
          ) : undefined
        }
      />

      <div className="flex flex-col gap-6 lg:flex-row lg:items-start">
        {(desktop || showList) && (
          <aside className="flex flex-col gap-4 lg:sticky lg:top-4 lg:w-72 lg:shrink-0">
            <SearchBox value={query} onChange={setQuery} />
            {searching ? (
              <SearchResults query={query} onPick={() => setQuery('')} />
            ) : (
              <SectionNav active={desktop ? (section ?? 'general') : undefined} compact={desktop} />
            )}
          </aside>
        )}

        {!showList && current && (
          <section aria-labelledby="settings-section-title" className="flex min-w-0 flex-1 flex-col gap-5">
            <div className={cn(!desktop && 'sr-only')}>
              <h2 id="settings-section-title" className="text-lg font-semibold tracking-tight text-fg">
                {current.title}
              </h2>
              <p className="text-sm text-fg-muted">{current.description}</p>
            </div>
            <div key={current.id} className="flex animate-fade-in flex-col gap-5">
              <SectionBody id={current.id} form={form} />
            </div>
            <SaveBar form={form} />
          </section>
        )}
      </div>
    </div>
  );
}
