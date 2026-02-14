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
  setUsername: (name: string) => void;
  setLanguage: (lang: Language) => void;
  setToken: (token: string | null) => void;
  clearToken: () => void;
  setModalOpen: (open: boolean) => void;
  logout: () => void;
  setDownloadPreferences: (prefs: Partial<DownloadPreferences>) => void;
  setDownloadLoading: (loading: boolean) => void;
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
    }),
    {
      name: "anime-app-storage",
      partialize: (state) => ({
        username: state.username,
        language: state.language,
        token: state.token,
        downloadPreferences: state.downloadPreferences,
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
