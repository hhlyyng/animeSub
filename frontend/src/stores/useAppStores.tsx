import { create } from "zustand";
import { persist } from "zustand/middleware";

type Language = "zh" | "en";

interface AppState {
  // 状态
  username: string;
  language: Language;
  
  // Actions
  setUsername: (name: string) => void;
  setLanguage: (lang: Language) => void;
  logout: () => void;
}

export const useAppStore = create<AppState>()(
  persist(
    (set) => ({
      // 初始状态
      username: "",
      language: "zh",
      
      // 设置用户名
      setUsername: (name) => set({ username: name }),
      
      // 设置语言
      setLanguage: (lang) => set({ language: lang }),
      
      // 退出登录
      logout: () => set({ username: "" }),
    }),
    {
      name: "anime-app-storage", // localStorage 的 key 名称
    }
  )
);