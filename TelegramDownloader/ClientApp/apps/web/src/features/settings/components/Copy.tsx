import { Button, cn, IconButton, toast } from '@tfm/ui';
import { Check, Copy } from 'lucide-react';
import { useState } from 'react';
import { encode } from 'uqr';
import { t } from '@/i18n';

async function copyText(value: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(value);
    toast.success(t('Copied to the clipboard'));
    return true;
  } catch {
    toast.error(t('Could not copy'));
    return false;
  }
}

/** Copy button with a short "copied" state. */
export function CopyButton({ value, iconOnly, size = 'sm' }: { value: string; iconOnly?: boolean; size?: 'xs' | 'sm' | 'md' }) {
  const [copied, setCopied] = useState(false);
  const run = async () => {
    if (await copyText(value)) {
      setCopied(true);
      setTimeout(() => setCopied(false), 1600);
    }
  };
  if (iconOnly) {
    return (
      <IconButton label={t('Copy')} size={size} variant="ghost" onClick={() => void run()}>
        {copied ? <Check /> : <Copy />}
      </IconButton>
    );
  }
  return (
    <Button variant="secondary" size={size} icon={copied ? <Check /> : <Copy />} onClick={() => void run()}>
      {t('Copy')}
    </Button>
  );
}

/** Read-only monospace value with a copy button. */
export function CopyField({ value, className, label }: { value: string; className?: string; label?: string }) {
  return (
    <div className={cn('flex min-w-0 items-center gap-1 rounded-lg border border-border bg-surface-2 py-0.5 pl-3 pr-0.5', className)}>
      <code aria-label={label} className="min-w-0 flex-1 truncate font-mono text-sm text-fg">
        {value}
      </code>
      <CopyButton value={value} iconOnly size="xs" />
    </div>
  );
}

/** QR code drawn as SVG (dark modules on a white tile, readable in any theme). */
export function QrCode({ value, size = 168, label }: { value: string; size?: number; label: string }) {
  const qr = encode(value, { ecc: 'M', border: 2 });
  const n = qr.size;
  let path = '';
  qr.data.forEach((row, y) =>
    row.forEach((on, x) => {
      if (on) path += `M${x} ${y}h1v1h-1z`;
    })
  );
  return (
    <svg role="img" aria-label={label} viewBox={`0 0 ${n} ${n}`} width={size} height={size} shapeRendering="crispEdges" className="rounded-xl bg-white p-1 shadow-soft">
      <path d={path} fill="#0b1120" />
    </svg>
  );
}
