import { BrowserRouter as Router, Routes, Route, useLocation } from 'react-router-dom';
import SideBar from "./SideBar";
import DownloadPage from "./content/DownLoadPage";
import HomeContent from "./content/HomePageContent";
import Setting from "./content/SettingPage";
import { useAppStore } from "../../stores/useAppStores";

// Route Content
const AppContent = () => {
  const location = useLocation();
  const language = useAppStore((state) => state.language);
  const setLanguage = useAppStore((state) => state.setLanguage);
  const isModalOpen = useAppStore((state) => state.isModalOpen);

  // 从路径映射到页面类型
  const getCurrentPageType = (): 'home' | 'setting' | 'download' => {
    switch (location.pathname) {
      case '/':
      case '/home':
        return 'home';
      case '/download':
        return 'download';
      case '/setting':
      case '/settings':
        return 'setting';
      default:
        return 'home';
    }
  };

  return (
    <div className="flex min-h-screen w-screen mx-auto overflow-hidden">
      {/* Sidebar */}
      <SideBar 
        language={language}
        currentPage={getCurrentPageType()}
        onLanguageChange={setLanguage}
      />
      
      {/* Main Content Area */}
      <main className={`flex-1 min-w-0 overflow-hidden flex justify-center transition-all duration-200 ${isModalOpen ? 'blur-md pointer-events-none' : ''}`}>
        <Routes>
          <Route path="/" element={<HomeContent />} />
          <Route path="/home" element={<HomeContent />} />
          <Route path="/download" element={<DownloadPage />} />
          <Route path="/setting" element={<Setting />} />
          <Route path="/settings" element={<Setting />} />
          {/* 404 Redirect to Homepage */}
          <Route path="*" element={<HomeContent />} />
        </Routes>
      </main>
    </div>
  );
};

const Homepage = () => {
  return (
    <Router>
      <AppContent />
    </Router>
  );
};

export default Homepage;
