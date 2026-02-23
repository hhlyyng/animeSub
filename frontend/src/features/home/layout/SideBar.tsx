import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { SidebarButton } from "./SideBarButton";
import HomeIcon from "../../../components/icons/HomeIcon";
import SettingIcon from "../../../components/icons/SettingIcon";
import DownloadIcon from "../../../components/icons/DownloadIcon";
import SearchIcon from "../../../components/icons/SearchIcon";
import { DefaultSideBar, CollapseSideBarArrow, ExpandSideBarArrow } from "../../../components/icons/SidebarIcon";
import GithubIcon from "../../../components/icons/GithubIcon";
import LanguageToggleIcon from "../../../components/icons/LanguageToggleIcon";
import LogoutIcon from "../../../components/icons/LogoutIcon";
import { useAppStore } from "../../../stores/useAppStores";

type PageType = "home" | "setting" | "download" | "search";

interface SideBarProps {
  language: "en" | "zh";
  currentPage: PageType;
  onLanguageChange: (lang: "en" | "zh") => void;
}

const text: Record<
  "zh" | "en",
  {
    home: string;
    search: string;
    settings: string;
    download: string;
    github: string;
    logout: string;
    languageSwitch: string;
  }
> = {
  zh: {
    home: "\u4e3b\u9875",
    search: "\u641c\u7d22",
    settings: "\u8bbe\u7f6e",
    download: "\u8ba2\u9605\u548c\u4e0b\u8f7d",
    github: "\u9879\u76ee\u5730\u5740",
    logout: "\u767b\u51fa",
    languageSwitch: "English",
  },
  en: {
    home: "Homepage",
    search: "Search",
    settings: "Setting",
    download: "Subscriptions & Downloads",
    github: "GitHub",
    logout: "Logout",
    languageSwitch: "\u5207\u6362\u4e2d\u6587",
  },
};

const SideBar = ({ language, currentPage, onLanguageChange }: SideBarProps) => {
  const navigate = useNavigate();
  const [isCollapsed, setIsCollapsed] = useState(false);
  const [isToggleHovered, setIsToggleHovered] = useState(false);
  const [showText, setShowText] = useState(true);
  const logout = useAppStore((state) => state.logout);
  const username = useAppStore((state) => state.username);

  const toggleSideBar = () => {
    setIsCollapsed((prev) => {
      const next = !prev;
      if (next) {
        setShowText(false);
      } else {
        setTimeout(() => setShowText(true), 120);
      }
      return next;
    });
  };

  const toggleLanguage = () => {
    onLanguageChange(language === "zh" ? "en" : "zh");
  };

  const navigateToPage = (page: PageType) => {
    switch (page) {
      case "home":
        navigate("/home");
        break;
      case "download":
        navigate("/download");
        break;
      case "setting":
        navigate("/setting");
        break;
      case 'search':
        navigate('/search');
        break;
    }
  };

  const getToggleIcon = () => {
    if (!isToggleHovered) {
      return <DefaultSideBar />;
    }
    return isCollapsed ? <ExpandSideBarArrow /> : <CollapseSideBarArrow />;
  };

  const currentLanguage = text[language] || text.en;

  return (
    <div
      className={`
        sidebar-div
        ${isCollapsed ? "sidebar-div--collapsed" : "sidebar-div--expanded"}
      `}
    >
      <div className="sidebar-header">
        <SidebarButton
          icon={getToggleIcon()}
          label=""
          variant="toggle"
          onClick={toggleSideBar}
          collapsed={isCollapsed}
          onToggleHover={setIsToggleHovered}
          showtext={showText}
          language={language}
        />
        {!isCollapsed && showText && <h2 className="flex-1 text-2xl font-bold">Anime-Sub</h2>}
      </div>

      <nav className="sidebar-block-div !gap-4 w-full flex-col">
        <SidebarButton
          icon={<HomeIcon />}
          label={currentLanguage.home}
          variant="action"
          collapsed={isCollapsed}
          onClick={() => navigateToPage("home")}
          showtext={showText}
          active={currentPage === "home"}
          language={language}
        />

        <SidebarButton
          icon={<SearchIcon />}
          label={currentLanguage.search}
          variant="action"
          collapsed={isCollapsed}
          onClick={() => navigateToPage('search')}
          showtext={showText}
          active={currentPage === 'search'}
          language={language}
        />

        <SidebarButton
          icon={<DownloadIcon />}
          label={currentLanguage.download}
          variant="action"
          collapsed={isCollapsed}
          onClick={() => navigateToPage("download")}
          showtext={showText}
          active={currentPage === "download"}
          language={language}
        />

        <SidebarButton
          icon={<SettingIcon />}
          label={currentLanguage.settings}
          variant="action"
          collapsed={isCollapsed}
          onClick={() => navigateToPage("setting")}
          showtext={showText}
          active={currentPage === "setting"}
          language={language}
        />
      </nav>

      <div className={`w-full px-2 py-3 gap-1 flex ${isCollapsed ? "flex-col gap-3" : "flex-row"}`}>
        <SidebarButton
          icon={<GithubIcon />}
          label={currentLanguage.github}
          variant="action"
          collapsed={isCollapsed}
          onClick={() => window.open("https://github.com/hhlyyng/anime-subscription.git", "_blank")}
          showtext={showText}
          language={language}
        />

        <SidebarButton
          icon={<LanguageToggleIcon />}
          label={currentLanguage.languageSwitch}
          variant="action"
          collapsed={isCollapsed}
          onClick={toggleLanguage}
          showtext={showText}
          language={language}
        />
      </div>

      <div className={`mt-auto flex w-full border-t-2 border-gray-300 px-3 py-3 ${isCollapsed ? "flex-col items-center gap-2" : "flex-row items-center"}`}>
        <div className="flex shrink-0 h-10 w-10 items-center justify-center rounded-full bg-gray-200 text-sm font-bold text-gray-700">
          {(username || "U").charAt(0).toUpperCase()}
        </div>
        {showText && !isCollapsed && (
          <>
            <span className="ml-2 flex-1 truncate text-base font-medium text-gray-800">{username || "User"}</span>
            <div className="mx-2 h-5 w-0.5 bg-gray-400" />
          </>
        )}
        <button
          type="button"
          onClick={() => {
            logout();
            navigate("/login", { replace: true });
          }}
          className={`flex h-8 w-8 shrink-0 items-center justify-center !rounded-md !border-0 !bg-transparent !p-0 text-gray-800 transition-colors hover:!bg-gray-200 hover:text-black ${!isCollapsed ? "ml-1" : ""}`}
          title={currentLanguage.logout}
        >
          <LogoutIcon className="h-5 w-5" />
        </button>
      </div>
    </div>
  );
};

export default SideBar;
