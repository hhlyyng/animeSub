import { create } from "zustand";
import { persist } from "zustand/middleware";

type Language = "zh" | "en";
type Resolution = "1080p" | "720p" | "4K" | "all";

interface DownloadPreferences {
  resolution: Resolution;
  subgroup: string;
  subtitleType: string;
}

interface AppState {
  username: string;
  language: Language;
  token: string | null;
  isModalOpen: boolean;
  downloadPreferences: DownloadPreferences;
  downloadLoading: boolean;
  randomFeedEnabled: boolean;
  randomFeedCount: number;
  setUsername: (name: string) => void;
  setLanguage: (lang: Language) => void;
  setToken: (token: string | null) => void;
  clearToken: () => void;
  setModalOpen: (open: boolean) => void;
  logout: () => void;
  setDownloadPreferences: (prefs: Partial<DownloadPreferences>) => void;
  setDownloadLoading: (loading: boolean) => void;
  setRandomFeedEnabled: (v: boolean) => void;
  setRandomFeedCount: (v: number) => void;
}

export const useAppStore = create<AppState>()(
  persist(
    (set) => ({
      username: "",
      language: "zh",
      token: null,
      isModalOpen: false,
      downloadPreferences: {
        resolution: "all",
        subgroup: "all",
        subtitleType: "all",
      },
      downloadLoading: false,
      randomFeedEnabled: false,
      randomFeedCount: 10,
      setUsername: (name) => set({ username: name }),
      setLanguage: (lang) => set({ language: lang }),
      setToken: (token) => set({ token }),
      clearToken: () => set({ token: null }),
      setModalOpen: (open) => set({ isModalOpen: open }),
      logout: () => set({ username: "", token: null }),
      setDownloadPreferences: (prefs) =>
        set((state) => ({
          downloadPreferences: { ...state.downloadPreferences, ...prefs },
        })),
      setDownloadLoading: (loading) => set({ downloadLoading: loading }),
      setRandomFeedEnabled: (v) => set({ randomFeedEnabled: v }),
      setRandomFeedCount: (v) => set({ randomFeedCount: Math.min(20, Math.max(1, v)) }),
    }),
    {
      name: "anime-app-storage",
      partialize: (state) => ({
        username: state.username,
        language: state.language,
        token: state.token,
        downloadPreferences: state.downloadPreferences,
        randomFeedEnabled: state.randomFeedEnabled,
        randomFeedCount: state.randomFeedCount,
      }),
      onRehydrateStorage: () => (state) => {
        if (state) {
          state.isModalOpen = false;
          state.downloadLoading = false;
        }
      },
    }
  )
);
