import type { FormValues } from '../model/configForm';
import type { SettingsForm } from '../model/useSettingsForm';

/** Props of a section that edits the config form (rendered once the config is loaded). */
export interface FormSectionProps {
  form: SettingsForm;
  values: FormValues;
}
