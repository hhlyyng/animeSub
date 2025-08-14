import { SidebarButton } from "./SideBarButton";
import React from "react";
import { useState } from "react";
import { useNavigate } from "react-router-dom";

// Icon SVG imports
import HomeIcon from "../icon/HomeIcon";
import SettingIcon from "../icon/SettingIcon";
import DownloadIcon from "../icon/DownloadIcon";
import { DefaultSideBar, CollapseSideBarArrow, ExpandSideBarArrow } from "../icon/SidebarIcon";
import GithubIcon from "../icon/GithubIcon";
import LogoutIcon from "../icon/LogoutIcon";
import LanguageToggleIcon from "../icon/LanguageToggleIcon";

type PageType = 'home' | 'setting' | 'download';

interface SideBarProps {
  language: "en" | "zh";
  currentPage: PageType;
  onLanguageChange: (lang: "en" | "zh") => void;
}

const SideBar: React.FC<SideBarProps> = ({ language, currentPage, onLanguageChange }) => {
  const navigate = useNavigate();
  const [isCollapsed, setIsCollapsed] = useState(false);
  const [isToggleHovered, setIsToggleHovered] = useState(false);
  const [showText, setShowText] = useState(true);

  const text: {
    [key: string]: {
      home: string;
      settings: string;
      download: string;
      github: string;
      logout: string;
      languageSwitch: string;
    }
  } = {
    zh: {
      home: "主页",
      settings: "设置",
      download: "下载",
      github: "项目地址",
      logout: "登出",
      languageSwitch: "English"
    },
    en: {
      home: "Homepage",
      settings: "Setting",
      download: "Download",
      github: "GitHub",
      logout: "Logout",
      languageSwitch: "切换中文"
    }
  };

  // Toggle sidebar function
  const toggleSideBar = () => {
    setIsCollapsed(prev => {
      const next = !prev;
  
      if (next) {
        setShowText(false);
      } else {
        setTimeout(() => setShowText(true), 120);
      }
  
      return next;
    });
  };

  // 切换语言
  const toggleLanguage = () => {
    onLanguageChange(language === "zh" ? "en" : "zh");
  };

  // 页面导航函数
  const navigateToPage = (page: PageType) => {
    switch (page) {
      case 'home':
        navigate('/home');
        break;
      case 'download':
        navigate('/download');
        break;
      case 'setting':
        navigate('/setting');
        break;
    }
  };

  // Get toggle icon based on hover and collapse state
  const getToggleIcon = () => {
    if (!isToggleHovered) {
      return <DefaultSideBar />;
    }
    return isCollapsed ? <ExpandSideBarArrow /> : <CollapseSideBarArrow />;
  };

  const currentLanguage = text[language] || text.en;

  return (
    <div className={`
        sidebar-div
        ${isCollapsed? "sidebar-div--collapsed" : "sidebar-div--expanded"}
        `}>
      {/* Toggle Button */}
      <div className="sidebar-header flex flex-row gap-3 !justify-start !pt-15">
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
        {!isCollapsed && showText && <h2 className="font-bold flex-1 text-2xl">Anime-Sub</h2>}
      </div>

      {/* Navigation Items */}
      <nav className="sidebar-block-div flex-col !gap-4 w-full">
        <SidebarButton
          icon={<HomeIcon />}
          label={currentLanguage.home}
          variant="action"
          collapsed={isCollapsed}
          onClick={() => navigateToPage('home')}
          showtext={showText}
          active={currentPage === 'home'}
          language={language}
        />

        <SidebarButton
          icon={<DownloadIcon />}
          label={currentLanguage.download}
          variant="action"
          collapsed={isCollapsed}
          onClick={() => navigateToPage('download')}
          showtext={showText}
          active={currentPage === 'download'}
          language={language}
        />
        
        <SidebarButton
          icon={<SettingIcon />}
          label={currentLanguage.settings}
          variant="action"
          collapsed={isCollapsed}
          onClick={() => navigateToPage('setting')}
          showtext={showText}
          active={currentPage === 'setting'}
          language={language}
        />
      </nav>

      {/* Bottom Actions */}
      <div className={`w-full px-2 py-3 gap-1 flex ${isCollapsed ? 'flex-col gap-3' : 'flex-row'}`}>
        <SidebarButton
          icon={<GithubIcon />}
          label={currentLanguage.github}
          variant="action"
          collapsed={isCollapsed}
          onClick={() => window.open('https://github.com/hhlyyng/anime-subscription.git', '_blank')}
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

      <div className="w-full px-2 py-3 gap-1 flex flex-row border-t border-[#e0e0e0] mt-auto">
        <div className="relative group !bg-blue-300 rounded-full flex items-center justify-center text-sm font-bold text-black shrink-0 w-[44px] h-[44px]">
         U
        </div>
        {showText && !isCollapsed }
      </div>
    </div>
  );
};

export default SideBar;