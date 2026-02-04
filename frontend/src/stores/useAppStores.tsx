import { create } from "zustand";
import { persist } from "zustand/middleware";

type Language = "zh" | "en";

interface AppState {
  // 状态
  username: string;
  language: Language;
  isModalOpen: boolean;

  // Actions
  setUsername: (name: string) => void;
  setLanguage: (lang: Language) => void;
  setModalOpen: (open: boolean) => void;
  logout: () => void;
}

export const useAppStore = create<AppState>()(
  persist(
    (set) => ({
      // 初始状态
      username: "",
      language: "zh",
      isModalOpen: false,

      // 设置用户名
      setUsername: (name) => set({ username: name }),

      // 设置语言
      setLanguage: (lang) => set({ language: lang }),

      // 设置 Modal 开关状态
      setModalOpen: (open) => set({ isModalOpen: open }),

      // 退出登录
      logout: () => set({ username: "" }),
    }),
    {
      name: "anime-app-storage", // localStorage 的 key 名称
      partialize: (state) => ({
        // Only persist these fields, exclude isModalOpen (UI state)
        username: state.username,
        language: state.language,
      }),
      // Ensure isModalOpen is always reset on page load
      onRehydrateStorage: () => (state) => {
        if (state) {
          state.isModalOpen = false;
        }
      },
    }
  )
);