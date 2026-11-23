import { isApiError, type SetupStatusDto } from '@tfm/api-client';
import { t } from '@/i18n';
import { errorMessage } from '@/lib/api';

export type WizardStep = 'mongo' | 'telegram' | 'password' | 'done';

/** First step to show for a setup status. */
export function initialStep(status: SetupStatusDto): WizardStep {
  if (status.currentStep === 'MongoDbRequired' || !status.mongoDbConnected) return 'mongo';
  if (status.currentStep === 'TelegramRequired' || !status.telegramConfigured) return 'telegram';
  return 'done';
}

export function validateMongo(value: string): string | null {
  const v = value.trim();
  if (!v) return t('Enter the connection string');
  if (!/^mongodb(\+srv)?:\/\/\S+$/i.test(v)) return t('It must start with mongodb:// or mongodb+srv://');
  return null;
}

export function validateApiId(value: string): string | null {
  const v = value.trim();
  if (!v) return t('Enter the api_id');
  if (!/^\d{3,12}$/.test(v)) return t('The api_id is a number');
  return null;
}

export function validateApiHash(value: string): string | null {
  const v = value.trim();
  if (!v) return t('Enter the api_hash');
  if (!/^[a-f0-9]{32}$/i.test(v)) return t('The api_hash has 32 hexadecimal characters');
  return null;
}

/** Message of a failed setup call, with the server detail when there is one. */
export function setupError(error: unknown): string {
  if (isApiError(error) && error.detail) return `${error.message}: ${error.detail}`;
  return errorMessage(error);
}
