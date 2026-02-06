import { create } from "zustand";
import { persist } from "zustand/middleware";

type Language = "zh" | "en";
type Resolution = '1080p' | '720p' | '4K' | 'all';
type SubtitleType = '简日内嵌' | '繁日' | '简体' | '繁体' | 'all';

interface DownloadPreferences {
  resolution: Resolution;
  subgroup: string;
  subtitleType: SubtitleType;
}

interface AppState {
  // 状态
  username: string;
  language: Language;
  isModalOpen: boolean;
  downloadPreferences: DownloadPreferences;
  downloadLoading: boolean;

  // Actions
  setUsername: (name: string) => void;
  setLanguage: (lang: Language) => void;
  setModalOpen: (open: boolean) => void;
  logout: () => void;
  setDownloadPreferences: (prefs: Partial<DownloadPreferences>) => void;
  setDownloadLoading: (loading: boolean) => void;
}

export const useAppStore = create<AppState>()(
  persist(
    (set) => ({
      // 初始状态
      username: "",
      language: "zh",
      isModalOpen: false,
      downloadPreferences: {
        resolution: 'all',
        subgroup: 'all',
        subtitleType: 'all'
      },
      downloadLoading: false,

      // 设置用户名
      setUsername: (name) => set({ username: name }),

      // 设置语言
      setLanguage: (lang) => set({ language: lang }),

      // 设置 Modal 开关状态
      setModalOpen: (open) => set({ isModalOpen: open }),

      // 退出登录
      logout: () => set({ username: "" }),

      // 设置下载偏好
      setDownloadPreferences: (prefs) => set((state) => ({
        downloadPreferences: { ...state.downloadPreferences, ...prefs }
      })),

      // 设置下载加载状态
      setDownloadLoading: (loading) => set({ downloadLoading: loading }),
    }),
    {
      name: "anime-app-storage", // localStorage 的 key 名称
      partialize: (state) => ({
        // Only persist these fields, exclude isModalOpen (UI state)
        username: state.username,
        language: state.language,
        downloadPreferences: state.downloadPreferences,
      }),
      // Ensure isModalOpen is always reset on page load
      onRehydrateStorage: () => (state) => {
        if (state) {
          state.isModalOpen = false;
          state.downloadLoading = false;
        }
      },
    }
  )
);
