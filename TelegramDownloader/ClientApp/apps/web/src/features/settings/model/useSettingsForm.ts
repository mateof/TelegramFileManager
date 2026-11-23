import type { AppConfigDto } from '@tfm/api-client';
import { toast } from '@tfm/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback, useMemo } from 'react';
import { create } from 'zustand';
import { useTelegramStatus } from '@/auth/useTelegramStatus';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import {
  adjustedFields,
  applyPatch,
  buildPatch,
  dirtyKeys,
  setDraftField,
  toFormValues,
  validate,
  type Draft,
  type FormKey,
  type FormValues
} from './configForm';

export const settingsKeys = {
  config: ['settings', 'config'] as const,
  imageCache: ['settings', 'image-cache'] as const,
  ffmpeg: ['settings', 'ffmpeg'] as const,
  version: ['settings', 'version'] as const,
  hidden: ['settings', 'hidden-channels'] as const,
  persisted: ['settings', 'persisted-count'] as const,
  libraryScan: ['settings', 'library-scan'] as const,
  channelFolders: (channelId: number) => ['settings', 'channel-folders', channelId] as const
};

/**
 * The unsaved changes of the settings form. Module level (not component
 * state) so they survive switching sections and remounts.
 */
interface DraftStore {
  draft: Draft;
  setDraft: (draft: Draft) => void;
  reset: () => void;
}

export const useSettingsDraft = create<DraftStore>()((set) => ({
  draft: {},
  setDraft: (draft) => set({ draft }),
  reset: () => set({ draft: {} })
}));

export function useConfigQuery() {
  return useQuery({ queryKey: settingsKeys.config, queryFn: ({ signal }) => api.config.get(signal), staleTime: 30_000 });
}

/** Form state on top of the config query: values, errors, dirty state and the save mutation. */
export function useSettingsForm() {
  const qc = useQueryClient();
  const config = useConfigQuery();
  const telegram = useTelegramStatus();
  const isPremium = telegram.data?.user?.isPremium ?? false;
  const draft = useSettingsDraft((s) => s.draft);

  const base = useMemo(() => (config.data ? toFormValues(config.data) : undefined), [config.data]);
  const values = useMemo<FormValues | undefined>(() => (base ? { ...base, ...draft } : undefined), [base, draft]);
  const errors = useMemo(() => (values ? validate(values, { isPremium }) : {}), [values, isPremium]);
  const dirty = useMemo(() => dirtyKeys(base, draft), [base, draft]);

  const setField = useCallback(
    <K extends FormKey>(key: K, value: FormValues[K]) => {
      const store = useSettingsDraft.getState();
      const currentBase = qc.getQueryData<AppConfigDto>(settingsKeys.config);
      store.setDraft(setDraftField(store.draft, currentBase ? toFormValues(currentBase) : undefined, key, value));
    },
    [qc]
  );

  const save = useMutation({
    mutationFn: (patch: ReturnType<typeof buildPatch>) => api.config.update(patch),
    onMutate: async (patch) => {
      await qc.cancelQueries({ queryKey: settingsKeys.config });
      const previous = qc.getQueryData<AppConfigDto>(settingsKeys.config);
      const previousDraft = useSettingsDraft.getState().draft;
      if (previous) qc.setQueryData(settingsKeys.config, applyPatch(previous, patch));
      useSettingsDraft.getState().reset();
      return { previous, previousDraft };
    },
    onError: (error, _patch, ctx) => {
      if (ctx?.previous) qc.setQueryData(settingsKeys.config, ctx.previous);
      if (ctx) useSettingsDraft.getState().setDraft(ctx.previousDraft);
      toast.error(t('Could not save the settings'), { description: errorMessage(error) });
    },
    onSuccess: (saved, patch) => {
      qc.setQueryData(settingsKeys.config, saved);
      const adjusted = adjustedFields(patch, saved);
      if (adjusted.length > 0) toast.info(t('Saved. The server adjusted some values to their valid range.'));
      else toast.success(t('Settings saved'));
    }
  });

  const { mutateAsync } = save;
  /** Saves the draft; resolves false when there was nothing valid to save or the save failed. */
  const submit = useCallback(async () => {
    if (!base || Object.keys(errors).length > 0) return false;
    const patch = buildPatch(base, useSettingsDraft.getState().draft);
    if (Object.keys(patch).length === 0) return false;
    try {
      await mutateAsync(patch);
      return true;
    } catch {
      return false;
    }
  }, [base, errors, mutateAsync]);

  return {
    config,
    values,
    base,
    errors,
    dirty,
    isDirty: dirty.length > 0,
    isPremium,
    setField,
    submit,
    saving: save.isPending,
    discard: useSettingsDraft.getState().reset
  };
}

export type SettingsForm = ReturnType<typeof useSettingsForm>;

/** Saves one config change right away (toggles outside the Save flow, e.g. the API key). */
export function useImmediateConfig() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (patch: Parameters<typeof api.config.update>[0]) => api.config.update(patch),
    onSuccess: (saved) => qc.setQueryData(settingsKeys.config, saved),
    onError: (error) => toast.error(t('Could not save the settings'), { description: errorMessage(error) })
  });
}
