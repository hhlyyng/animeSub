import { SidebarButton } from "./SideBarButton";
import React from "react";
import { useState } from "react";

// Icon SVG imports
import HomeIcon from "../icon/HomeIcon";
import SettingIcon from "../icon/SettingIcon";
import DownloadIcon from "../icon/DownloadIcon";
import { DefaultSideBar, CollapseSideBarArrow, ExpandSideBarArrow } from "../icon/SidebarIcon";
import GithubIcon from "../icon/GithubIcon";
import LogoutIcon from "../icon/LogoutIcon";
import LanguageToggleIcon from "../icon/LanguageToggleIcon";

interface SideBarProps {
  language: "en" | "zh";
}

const SideBar: React.FC<SideBarProps> = ({language}) => {
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
      logout: "退出",
      languageSwitch: "切换英文"
    },
    en: {
      home: "Homepage",
      settings: "Setting",
      download: "Download",
      github: "GitHub Repo Address",
      logout: "Logout",
      languageSwitch:"Switch to Chinese"
    }
  };

  // Toggle sidebar function
  const toggleSideBar = () => {
    setIsCollapsed(prev => {
      const next = !prev;           // 切换目标：折叠⇄展开
  
      if (next) {
        // 正在折叠：先隐藏文字，再收起
        setShowText(false);
      } else {
        // 正在展开：等宽度动画结束再显示文字
        setTimeout(() => setShowText(true), 130);
      }
  
      return next;
    });
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
      <div className="sidebar-header flex flex-row gap-3 !justify-start !pt-8">
        <SidebarButton
          icon={getToggleIcon()}
          label=""
          variant="toggle"
          onClick={toggleSideBar}
          collapsed={isCollapsed}
          onToggleHover={setIsToggleHovered}
          showtext={showText}
        />
        {!isCollapsed && <h2 className="font-bold flex-1 text-2xl">Anime-Sub</h2>}
      </div>

      {/* Navigation Items */}
      <nav className="sidebar-block-div flex-col !gap-4 w-full !pb-40">
        <SidebarButton
          icon={<HomeIcon />}
          label={currentLanguage.home}
          variant="action"
          collapsed={isCollapsed}
          onClick={() => console.log('Home clicked')}
          showtext={showText}
        />

        <SidebarButton
          icon={<DownloadIcon />}
          label={currentLanguage.download}
          variant="action"
          collapsed={isCollapsed}
          onClick={() => console.log('Download clicked')}
          showtext={showText}
        />
        
        <SidebarButton
          icon={<SettingIcon />}
          label={currentLanguage.settings}
          variant="action"
          collapsed={isCollapsed}
          onClick={() => console.log('Settings clicked')}
          showtext={showText}
        />
        

      </nav>

      {/* Bottom Actions */}
      <div className={`w-full px-2 py-2 gap-2 flex ${isCollapsed ? 'flex-col' : 'flex-row'}`}>
        <SidebarButton
          icon={<GithubIcon />}
          label={currentLanguage.github}
          variant="action"
          collapsed={isCollapsed}
          onClick={() => window.open('https://github.com', '_blank')}
          showtext={showText}
        />
        
        <SidebarButton
          icon={<LanguageToggleIcon />}
          label={currentLanguage.languageSwitch}
          variant="action"
          collapsed={isCollapsed}
          onClick={() => console.log('Logout clicked')}
          showtext={showText}
        />
      </div>
    </div>
  );
};

export default SideBar;



