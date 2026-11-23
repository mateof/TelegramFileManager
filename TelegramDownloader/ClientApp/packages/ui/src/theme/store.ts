import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import type { AccentId, BackgroundSetting } from './palettes';

export type MotionSetting = 'system' | 'full' | 'reduced';
export type Density = 'comfortable' | 'compact';

export interface AppearanceState {
  accent: AccentId;
  background: BackgroundSetting;
  /** Frosted glass surfaces (backdrop blur). Off = opaque surfaces (faster on weak GPUs). */
  glass: boolean;
  /** Soft accent-tinted gradients behind the content. */
  ambient: boolean;
  motion: MotionSetting;
  density: Density;
  setAccent: (accent: AccentId) => void;
  setBackground: (background: BackgroundSetting) => void;
  setGlass: (on: boolean) => void;
  setAmbient: (on: boolean) => void;
  setMotion: (motion: MotionSetting) => void;
  setDensity: (density: Density) => void;
  /** Quick toggle used by the top bar: light ↔ the last dark background. */
  toggleLight: () => void;
  lastDark: Exclude<BackgroundSetting, 'light' | 'system'>;
}

export const APPEARANCE_STORAGE_KEY = 'tfm.web.appearance';

export const useAppearance = create<AppearanceState>()(
  persist(
    (set, get) => ({
      accent: 'indigo',
      background: 'system',
      glass: true,
      ambient: true,
      motion: 'system',
      density: 'comfortable',
      lastDark: 'slate',
      setAccent: (accent) => set({ accent }),
      setBackground: (background) =>
        set(background !== 'light' && background !== 'system' ? { background, lastDark: background } : { background }),
      setGlass: (glass) => set({ glass }),
      setAmbient: (ambient) => set({ ambient }),
      setMotion: (motion) => set({ motion }),
      setDensity: (density) => set({ density }),
      toggleLight: () => {
        const { background, lastDark } = get();
        const isLight =
          background === 'light' ||
          (background === 'system' && typeof window !== 'undefined' && window.matchMedia?.('(prefers-color-scheme: light)').matches);
        set({ background: isLight ? lastDark : 'light' });
      }
    }),
    {
      name: APPEARANCE_STORAGE_KEY,
      partialize: ({ accent, background, glass, ambient, motion, density, lastDark }) => ({
        accent,
        background,
        glass,
        ambient,
        motion,
        density,
        lastDark
      })
    }
  )
);
