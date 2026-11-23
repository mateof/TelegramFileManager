import { ACCENTS, BACKGROUNDS, cn, SegmentedControl, Switch, useAppearance, type BackgroundSetting } from '@tfm/ui';
import { Check, Languages, Monitor, Palette, Sparkles } from 'lucide-react';
import { useId } from 'react';
import { getLanguage, LANGUAGES, setLanguage, t, type Language } from '@/i18n';
import { SettingRow, SettingsGroup } from '../components/SettingRow';

function accentLabel(id: string): string {
  const labels: Record<string, string> = {
    indigo: t('Indigo'),
    violet: t('Violet'),
    ocean: t('Ocean'),
    teal: t('Teal'),
    emerald: t('Emerald'),
    rose: t('Rose'),
    sunset: t('Sunset'),
    amber: t('Amber')
  };
  return labels[id] ?? id;
}

function backgroundLabel(id: BackgroundSetting): string {
  const labels: Record<BackgroundSetting, string> = {
    system: t('System'),
    slate: t('Slate'),
    graphite: t('Graphite'),
    midnight: t('Midnight'),
    amoled: t('AMOLED black'),
    light: t('Light')
  };
  return labels[id];
}

/** Appearance of this browser (persisted locally by the @tfm/ui theme store). */
export function AppearanceSection() {
  const a = useAppearance();
  const glassId = useId();
  const ambientId = useId();
  const themes: { id: BackgroundSetting; swatch: string[] }[] = [
    { id: 'system', swatch: ['#0b1120', '#f8fafc'] },
    ...BACKGROUNDS.map((b) => ({ id: b.id as BackgroundSetting, swatch: [b.palette[950], b.palette[800], b.palette[600]] }))
  ];

  return (
    <>
      <SettingsGroup title={t('Theme')} icon={<Monitor />} description={t('These options are stored in this browser only.')}>
        <div id="setting-theme" className="grid grid-cols-2 gap-3 p-4 sm:grid-cols-3">
          {themes.map((theme) => {
            const active = a.background === theme.id;
            return (
              <button
                key={theme.id}
                type="button"
                aria-pressed={active}
                onClick={() => a.setBackground(theme.id)}
                className={cn(
                  'group flex flex-col gap-2 rounded-xl border p-2 text-left transition-all duration-200 hover:-translate-y-0.5 focus-visible:outline-2 focus-visible:outline-ring',
                  active ? 'border-accent-500 bg-primary-soft shadow-glow' : 'border-border hover:border-border-strong'
                )}
              >
                <span className="flex h-14 overflow-hidden rounded-lg ring-1 ring-black/10">
                  {theme.swatch.map((c, i) => (
                    <span key={i} className="flex-1" style={{ background: c }} />
                  ))}
                </span>
                <span className="flex items-center justify-between px-1 text-sm text-fg">
                  {backgroundLabel(theme.id)}
                  {active && <Check className="size-4 text-accent-400" />}
                </span>
              </button>
            );
          })}
        </div>
      </SettingsGroup>

      <SettingsGroup title={t('Accent colour')} icon={<Palette />}>
        <div id="setting-accent" role="radiogroup" aria-label={t('Accent colour')} className="flex flex-wrap gap-3 p-4">
          {ACCENTS.map((accent) => {
            const active = a.accent === accent.id;
            return (
              <button
                key={accent.id}
                type="button"
                role="radio"
                aria-checked={active}
                aria-label={accentLabel(accent.id)}
                title={accentLabel(accent.id)}
                onClick={() => a.setAccent(accent.id)}
                className={cn(
                  'flex size-11 items-center justify-center rounded-full ring-offset-2 ring-offset-bg transition-transform duration-200 hover:scale-110 focus-visible:outline-2 focus-visible:outline-ring',
                  active && 'ring-2 ring-fg'
                )}
                style={{ background: `linear-gradient(135deg, ${accent.palette[400]}, ${accent.palette[600]})`, color: accent.onAccent }}
              >
                {active && <Check className="size-5" strokeWidth={3} />}
              </button>
            );
          })}
        </div>
      </SettingsGroup>

      <SettingsGroup title={t('Effects')} icon={<Sparkles />}>
        <SettingRow id="effects" label={t('Glass surfaces')} controlId={glassId} extra={<p className="mt-1 text-xs text-fg-muted">{t('Frosted, translucent panels. Turn it off on slow devices.')}</p>}>
          <Switch id={glassId} checked={a.glass} onCheckedChange={a.setGlass} />
        </SettingRow>
        <SettingRow id="ambient" label={t('Ambient background')} controlId={ambientId} extra={<p className="mt-1 text-xs text-fg-muted">{t('Soft colour gradients behind the content.')}</p>}>
          <Switch id={ambientId} checked={a.ambient} onCheckedChange={a.setAmbient} />
        </SettingRow>
        <SettingRow id="motion" label={t('Animations')} extra={<p className="mt-1 text-xs text-fg-muted">{t('"System" follows the reduced motion preference of the device.')}</p>}>
          <SegmentedControl
            value={a.motion}
            onValueChange={a.setMotion}
            options={[
              { value: 'system', label: t('System') },
              { value: 'full', label: t('Full') },
              { value: 'reduced', label: t('Reduced') }
            ]}
            size="sm"
          />
        </SettingRow>
        <SettingRow id="density">
          <SegmentedControl
            value={a.density}
            onValueChange={a.setDensity}
            options={[
              { value: 'comfortable', label: t('Comfortable') },
              { value: 'compact', label: t('Compact') }
            ]}
            size="sm"
          />
        </SettingRow>
      </SettingsGroup>

      <SettingsGroup title={t('Language')} icon={<Languages />}>
        <SettingRow id="language" extra={<p className="mt-1 text-xs text-fg-muted">{t('The page reloads to apply the language.')}</p>}>
          <SegmentedControl<Language> value={getLanguage()} onValueChange={setLanguage} options={LANGUAGES.map((l) => ({ value: l.code, label: l.label }))} size="sm" />
        </SettingRow>
      </SettingsGroup>
    </>
  );
}
