import { forwardRef, type InputHTMLAttributes, type ReactNode, type TextareaHTMLAttributes } from 'react';
import { FIELD } from '../lib/classes';
import { cn } from '../lib/cn';

export interface InputProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'size'> {
  /** Icon inside the field, on the left. */
  leading?: ReactNode;
  /** Element inside the field, on the right (clear button, Kbd...). */
  trailing?: ReactNode;
  inputSize?: 'sm' | 'md' | 'lg';
}

const INPUT_SIZES = { sm: 'h-8 px-2.5', md: 'h-9.5 px-3', lg: 'h-11 px-3.5 text-base' } as const;

export const Input = forwardRef<HTMLInputElement, InputProps>(function Input(
  { className, leading, trailing, inputSize = 'md', ...props },
  ref
) {
  if (!leading && !trailing) {
    return <input ref={ref} className={cn(FIELD, INPUT_SIZES[inputSize], className)} {...props} />;
  }
  return (
    <div className={cn('relative flex items-center', className)}>
      {leading && (
        <span className="pointer-events-none absolute left-3 flex text-fg-subtle [&_svg]:size-4">{leading}</span>
      )}
      <input
        ref={ref}
        className={cn(FIELD, INPUT_SIZES[inputSize], leading && 'pl-9', trailing && 'pr-10')}
        {...props}
      />
      {trailing && <span className="absolute right-2 flex items-center">{trailing}</span>}
    </div>
  );
});

export const Textarea = forwardRef<HTMLTextAreaElement, TextareaHTMLAttributes<HTMLTextAreaElement>>(function Textarea(
  { className, ...props },
  ref
) {
  return <textarea ref={ref} className={cn(FIELD, 'min-h-20 px-3 py-2', className)} {...props} />;
});
