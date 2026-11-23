import { Card, cn, Input, Switch } from '@tfm/ui';
import { useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { useLocation } from 'react-router';
import { anchorOf, settingDef } from '../catalog';

/** Card grouping several setting rows under a title. */
export function SettingsGroup({ title, description, icon, children, actions }: { title?: ReactNode; description?: ReactNode; icon?: ReactNode; children: ReactNode; actions?: ReactNode }) {
  return (
    <Card className="overflow-hidden">
      {(title || actions) && (
        <div className="flex items-start justify-between gap-3 border-b border-border px-5 py-4">
          <div className="flex min-w-0 items-start gap-3">
            {icon && <span className="mt-0.5 text-accent-400 [&_svg]:size-4.5">{icon}</span>}
            <div className="min-w-0">
              {title && <h2 className="text-sm font-semibold text-fg">{title}</h2>}
              {description && <p className="mt-0.5 text-xs text-fg-muted">{description}</p>}
            </div>
          </div>
          {actions && <div className="flex shrink-0 items-center gap-2">{actions}</div>}
        </div>
      )}
      <div className="divide-y divide-border">{children}</div>
    </Card>
  );
}

export interface SettingRowProps {
  /** Id in the catalog (label, description, hint and anchor come from there). */
  id: string;
  /** Control on the right (inline) or below the text (stacked). */
  children?: ReactNode;
  error?: string;
  stacked?: boolean;
  /** Small element next to the label (badge). */
  badge?: ReactNode;
  /** Extra content below the description. */
  extra?: ReactNode;
  /** Override the catalog label. */
  label?: ReactNode;
  className?: string;
  /** id of the control, to wire the label. */
  controlId?: string;
}

/** One setting: label, help text, defaults line and the control. Deep-linkable with `#setting-<id>`. */
export function SettingRow({ id, children, error, stacked, badge, extra, label, className, controlId }: SettingRowProps) {
  const def = settingDef(id);
  const anchor = anchorOf(id);
  const location = useLocation();
  const ref = useRef<HTMLDivElement>(null);

  // Deep link (#setting-<id>): scroll to the row and flash it
  useEffect(() => {
    const el = ref.current;
    if (location.hash !== `#${anchor}` || !el) return;
    el.scrollIntoView({ block: 'center', behavior: 'smooth' });
    el.classList.add('bg-primary-soft');
    const timer = setTimeout(() => el.classList.remove('bg-primary-soft'), 2200);
    return () => {
      clearTimeout(timer);
      el.classList.remove('bg-primary-soft');
    };
  }, [location.hash, location.key, anchor]);

  const errorId = useId();
  const text = (
    <div className="min-w-0 flex-1">
      <div className="flex flex-wrap items-center gap-2">
        {controlId ? (
          <label htmlFor={controlId} className="text-sm font-medium text-fg">
            {label ?? def?.label}
          </label>
        ) : (
          <h3 className="text-sm font-medium text-fg">{label ?? def?.label}</h3>
        )}
        {badge}
      </div>
      {def?.description && <p className="mt-1 text-xs leading-relaxed text-fg-muted">{def.description}</p>}
      {def?.hint && <p className="mt-1 text-[11px] text-fg-subtle">{def.hint}</p>}
      {extra}
      {error && (
        <p id={errorId} role="alert" className="mt-1.5 text-xs font-medium text-danger">
          {error}
        </p>
      )}
    </div>
  );

  return (
    <div
      id={anchor}
      ref={ref}
      className={cn(
        'scroll-mt-24 px-5 py-4 transition-colors duration-700',
        stacked ? 'flex flex-col gap-3' : 'flex flex-col gap-3 sm:flex-row sm:items-center sm:gap-6',
        className
      )}
    >
      {text}
      {children && <div className={cn(stacked ? 'w-full' : 'shrink-0 sm:max-w-[55%]')}>{children}</div>}
    </div>
  );
}

/** Row with a switch bound to a boolean. */
export function SwitchRow({ id, checked, onChange, disabled, badge, extra, error }: { id: string; checked: boolean; onChange: (v: boolean) => void; disabled?: boolean; badge?: ReactNode; extra?: ReactNode; error?: string }) {
  const controlId = useId();
  return (
    <SettingRow id={id} controlId={controlId} badge={badge} extra={extra} error={error}>
      <Switch id={controlId} checked={checked} onCheckedChange={onChange} disabled={disabled} aria-label={settingDef(id)?.label} />
    </SettingRow>
  );
}

/** Numeric input with min/max and a unit. Keeps the raw text while typing. */
export function NumberInput({
  value,
  onChange,
  min,
  max,
  step = 1,
  unit,
  id,
  disabled,
  invalid,
  className
}: {
  value: number;
  onChange: (v: number) => void;
  min?: number;
  max?: number;
  step?: number;
  unit?: string;
  id?: string;
  disabled?: boolean;
  invalid?: boolean;
  className?: string;
}) {
  const [text, setText] = useState(String(value));
  const [lastValue, setLastValue] = useState(value);
  if (!Object.is(value, lastValue)) {
    // The value changed from outside (discard, server answer): show it
    setLastValue(value);
    if (!Number.isNaN(value) && Number(text) !== value) setText(String(value));
  }
  return (
    <Input
      id={id}
      type="number"
      inputMode={step < 1 ? 'decimal' : 'numeric'}
      min={min}
      max={max}
      step={step}
      value={text}
      disabled={disabled}
      aria-invalid={invalid || undefined}
      onChange={(e) => {
        setText(e.target.value);
        const n = e.target.value.trim() === '' ? Number.NaN : Number(e.target.value);
        onChange(n);
      }}
      className={cn('w-36', className)}
      trailing={unit ? <span className="pr-1 text-xs text-fg-subtle">{unit}</span> : undefined}
    />
  );
}

/** Row with a NumberInput. */
export function NumberRow({ id, value, onChange, min, max, step, unit, error, disabled }: { id: string; value: number; onChange: (v: number) => void; min?: number; max?: number; step?: number; unit?: string; error?: string; disabled?: boolean }) {
  const controlId = useId();
  return (
    <SettingRow id={id} controlId={controlId} error={error}>
      <NumberInput id={controlId} value={value} onChange={onChange} min={min} max={max} step={step} unit={unit} invalid={!!error} disabled={disabled} />
    </SettingRow>
  );
}
