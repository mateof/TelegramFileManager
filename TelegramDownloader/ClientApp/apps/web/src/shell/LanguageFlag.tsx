import { useId } from 'react';
import type { Language } from '@/i18n';

/**
 * Small flag for a language (inline SVG: emoji flags don't render on Windows
 * and Galicia has none). Decorative: the language name is always next to it.
 * The sizes are !important because menus and segmented controls size every
 * svg as a square icon.
 */
export function LanguageFlag({ lang, className }: { lang: Language; className?: string }) {
  const id = useId();
  return (
    <svg
      viewBox="0 0 30 20"
      aria-hidden="true"
      focusable="false"
      data-flag={lang}
      className={['h-3.5! w-[21px]! shrink-0 overflow-hidden rounded-[3px] ring-1 ring-border', className].filter(Boolean).join(' ')}
      preserveAspectRatio="xMidYMid slice"
    >
      {lang === 'es' && (
        <>
          <rect width="30" height="20" fill="#AA151B" />
          <rect y="5" width="30" height="10" fill="#F1BF00" />
        </>
      )}
      {lang === 'gl' && (
        <>
          <clipPath id={`${id}-gl`}>
            <rect width="30" height="20" />
          </clipPath>
          <rect width="30" height="20" fill="#FFFFFF" />
          {/* Sky-blue band from the upper hoist corner to the lower fly corner */}
          <path d="M0 0 L30 20" stroke="#0099CC" strokeWidth="5" clipPath={`url(#${id}-gl)`} />
        </>
      )}
      {lang === 'en' && (
        <g transform="scale(0.5 0.6667)">
          <clipPath id={`${id}-uk`}>
            <path d="M30 15h30v15zv15H0zH0V0zV0h30z" />
          </clipPath>
          <rect width="60" height="30" fill="#012169" />
          <path d="M0 0l60 30m0-30L0 30" stroke="#FFFFFF" strokeWidth="6" />
          <path d="M0 0l60 30m0-30L0 30" stroke="#C8102E" strokeWidth="4" clipPath={`url(#${id}-uk)`} />
          <path d="M30 0v30M0 15h60" stroke="#FFFFFF" strokeWidth="10" />
          <path d="M30 0v30M0 15h60" stroke="#C8102E" strokeWidth="6" />
        </g>
      )}
    </svg>
  );
}
