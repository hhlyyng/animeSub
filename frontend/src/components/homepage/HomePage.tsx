import SideBar from "./SideBar";
import DownloadPage from "./content/DownLoadPage";
import HomeContent from "./content/HomePageContent";
import Setting from "./content/SettingPage";

import { useState } from 'react';
type PageType = 'home' | 'setting' | 'download';

const Homepage = () => {
    const [currentPage, setCurrentPage] = useState<PageType>('home');
    const [language, setLanguage] = useState<"en" | "zh">("zh");

    const CurrentRenderPage = () => {
        switch (currentPage) {
            case 'home':
                return <HomeContent />;
            case 'download':
                return <DownloadPage />;
            case 'setting':
                return <Setting />;
            default:
                return <HomeContent />;
        }
    };

    return (
        <div className="flex min-h-screen">
          {/* Sidebar */}
          <SideBar 
            language={language}
          />
          
          {/* Main Content Area */}
          <main className="flex">
            {CurrentRenderPage()}
          </main>
        </div>
      );

}

export default Homepage;
