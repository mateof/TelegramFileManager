/**
 * Built-in labels of the components (close buttons, confirm dialog...). English
 * by default; the app passes translated strings once at startup with
 * `configureUiStrings({ close: t('Close'), ... })`.
 */
export interface UiStrings {
  close: string;
  cancel: string;
  confirm: string;
  loading: string;
  noResults: string;
  searchPlaceholder: string;
  breadcrumb: string;
  more: string;
}

export const uiStrings: UiStrings = {
  close: 'Close',
  cancel: 'Cancel',
  confirm: 'Confirm',
  loading: 'Loading...',
  noResults: 'No results',
  searchPlaceholder: 'Type a command or search...',
  breadcrumb: 'Breadcrumb',
  more: 'More'
};

export function configureUiStrings(strings: Partial<UiStrings>): void {
  Object.assign(uiStrings, strings);
}
