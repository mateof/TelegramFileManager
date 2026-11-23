import { clsx, type ClassValue } from 'clsx';
import { twMerge } from 'tailwind-merge';

/** Joins class names and resolves Tailwind conflicts (`cn('px-2', cond && 'px-4')`). */
export function cn(...inputs: ClassValue[]): string {
  return twMerge(clsx(inputs));
}
