import { Routes, Route, useLocation } from 'react-router-dom';
import SideBar from "./SideBar";
import HomeContent from "../components/HomePageContent";
import Setting from "../components/SettingPage";
import MySubscriptionDownloadPage from "../components/MySubscriptionDownloadPage";
import SearchPage from "../components/SearchPage";
import { useAppStore } from "../../../stores/useAppStores";

// Route Content
const AppContent = () => {
  const location = useLocation();
  const language = useAppStore((state) => state.language);
  const setLanguage = useAppStore((state) => state.setLanguage);
  const isModalOpen = useAppStore((state) => state.isModalOpen);

  const getCurrentPageType = (): 'home' | 'setting' | 'download' | 'search' => {
    switch (location.pathname) {
      case '/':
      case '/home':
        return 'home';
      case '/download':
        return 'download';
      case '/setting':
      case '/settings':
        return 'setting';
      case '/search':
        return 'search';
      default:
        return 'home';
    }
  };

  return (
    <div className="flex h-screen w-screen mx-auto overflow-hidden">
      <SideBar
        language={language}
        currentPage={getCurrentPageType()}
        onLanguageChange={setLanguage}
      />

      <main className={`flex-1 min-w-0 overflow-y-auto overflow-x-hidden flex justify-center transition-all duration-200 ${isModalOpen ? 'blur-md pointer-events-none' : ''}`}>
        <Routes>
          <Route path="/" element={<HomeContent />} />
          <Route path="/home" element={<HomeContent />} />
          <Route path="/download" element={<MySubscriptionDownloadPage />} />
          <Route path="/setting" element={<Setting />} />
          <Route path="/settings" element={<Setting />} />
          <Route path="/search" element={<SearchPage />} />
          <Route path="*" element={<HomeContent />} />
        </Routes>
      </main>
    </div>
  );
};

const Homepage = () => {
  return <AppContent />;
};

export default Homepage;
