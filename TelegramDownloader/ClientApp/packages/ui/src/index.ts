// @tfm/ui — design system. Styles: `@import "@tfm/ui/styles.css"` after tailwindcss.
// One module per component, so the app only bundles what it imports.

export { cn } from './lib/cn';
export { configureUiStrings, uiStrings, type UiStrings } from './lib/strings';

// Theme engine
export * from './theme/palettes';
export { useAppearance, APPEARANCE_STORAGE_KEY, type AppearanceState, type Density, type MotionSetting } from './theme/store';
export { ThemeController, useMediaQuery, useReducedMotion, useResolvedTheme } from './theme/ThemeController';

// Components
export * from './components/Button';
export * from './components/Spinner';
export * from './components/Card';
export * from './components/Badge';
export * from './components/Skeleton';
export * from './components/Kbd';
export * from './components/EmptyState';
export * from './components/Progress';
export * from './components/Avatar';
export * from './components/Field';
export * from './components/Input';
export * from './components/Select';
export * from './components/Switch';
export * from './components/Checkbox';
export * from './components/RadioGroup';
export * from './components/Slider';
export * from './components/SegmentedControl';
export * from './components/Dialog';
export * from './components/Sheet';
export * from './components/AlertDialog';
export * from './components/Popover';
export * from './components/Tooltip';
export * from './components/DropdownMenu';
export * from './components/ContextMenu';
export * from './components/Tabs';
export * from './components/ScrollArea';
export * from './components/Breadcrumb';
export * from './components/Toaster';
export * from './components/CommandPalette';
export type { MenuItemExtras } from './components/MenuShortcut';
