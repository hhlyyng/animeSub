import { useState } from "react";

const LoginBlock = () => {
  // State management for form inputs and UI controls
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [language, setLanguage] = useState("zh"); // 'zh' for Chinese, 'en' for English

  // Language text configurations
  const texts: {
    [key: string]: {
      title: string;
      usernamePlaceholder: string;
      passwordPlaceholder: string;
      loginButton: string;
      resetButton: string;
      welcomeMessage: string;
      emptyFieldsWarning: string;
    }
  } = {
    zh: {
      title: "动漫订阅",
      usernamePlaceholder: "请输入用户名",
      passwordPlaceholder: "请输入密码",
      loginButton: "登录",
      resetButton: "重置",
      welcomeMessage: "欢迎登录",
      emptyFieldsWarning: "请输入用户名和密码"
    },
    en: {
      title: "Anime Subscription",
      usernamePlaceholder: "Enter username",
      passwordPlaceholder: "Enter password",
      loginButton: "Login",
      resetButton: "Reset",
      welcomeMessage: "Welcome",
      emptyFieldsWarning: "Please enter username and password"
    }
  };

  // Get current language text
  const currentText = texts[language];

  // Handle login functionality
  const handleLogin = () => {
    console.log("Logging in with:", username, password);
    if (username && password) {
      alert(`${currentText.welcomeMessage}, ${username}！`);
    } else {
      alert(currentText.emptyFieldsWarning);
    }
  };

  // Handle reset functionality
  const handleReset = () => {
    setUsername("");
    setPassword("");
    setShowPassword(false);
  };

  // Toggle language between Chinese and English
  const toggleLanguage = () => {
    setLanguage(language === "zh" ? "en" : "zh");
  };

  // Eye icon SVG for password visibility toggle
  const EyeIcon = ({ isOpen }: { isOpen: boolean }) => (
    <svg
      width="20"
      height="20"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      {isOpen ? (
        // Open eye icon (password visible)
        <>
          <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z" />
          <circle cx="12" cy="12" r="3" />
        </>
      ) : (
        // Closed eye icon (password hidden)
        <>
          <path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24" />
          <line x1="1" y1="1" x2="23" y2="23" />
        </>
      )}
    </svg>
  );

  return (
    // Main container with full viewport height and responsive padding
    <div
      style={{
        display: "flex",
        justifyContent: "center",
        alignItems: "center",
        minHeight: "100vh",
        backgroundColor: "#f5f5f5", // Background color - can be customized
        padding: "20px",
        position: "relative"
      }}
    >
      {/* Language toggle button positioned at top-right */}
      <button
        onClick={toggleLanguage}
        style={{
          position: "absolute",
          top: "20px",
          right: "20px",
          padding: "8px 16px",
          backgroundColor: "#4CAF50", // 🎨 COLOR SETTING: Language button background
          color: "white",
          border: "none",
          borderRadius: "20px",
          cursor: "pointer",
          fontSize: "14px",
          fontWeight: "500",
          transition: "background-color 0.3s",
          zIndex: 10
        }}
        onMouseEnter={(e) => (e.target as HTMLButtonElement).style.backgroundColor = "#45a049"} // 🎨 COLOR SETTING: Language button hover
        onMouseLeave={(e) => (e.target as HTMLButtonElement).style.backgroundColor = "#4CAF50"} // 🎨 COLOR SETTING: Language button normal
      >
        {language === "zh" ? "EN" : "中文"}
      </button>

      {/* Login form container with responsive sizing */}
      <div
        style={{
          backgroundColor: "white", // 🎨 COLOR SETTING: Form background
          padding: "2.5rem",
          borderRadius: "12px",
          boxShadow: "0px 6px 20px rgba(0,0,0,0.15)", // 🎨 COLOR SETTING: Form shadow
          width: "100%",
          minWidth: "320px",      // Minimum width for mobile devices
          maxWidth: "420px",      // Maximum width for desktop
          minHeight: "480px",     // Minimum height to maintain proportions
          maxHeight: "600px",     // Maximum height to prevent oversizing
          display: "flex",
          flexDirection: "column",
          alignItems: "center",
          justifyContent: "center"
        }}
      >
        {/* Logo and title section */}
        <div style={{ marginBottom: "30px", textAlign: "center" }}>
          {/* 🖼️ LOGO SETTING: Replace this div with <img src="/your-logo.png" alt="Logo" style={{width: "60px", height: "60px", borderRadius: "50%"}} /> */}
          <div
            style={{
              width: "60px",
              height: "60px",
              backgroundColor: "#4CAF50", // 🎨 COLOR SETTING: Logo background (if using placeholder)
              borderRadius: "50%",
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
              margin: "0 auto 15px",
              fontSize: "24px",
              color: "white", // 🎨 COLOR SETTING: Logo text color
              fontWeight: "bold",
            }}
          >
            A {/* 🖼️ LOGO SETTING: This letter will be replaced by your logo image */}
          </div>
          <h2 style={{ 
            margin: 0, 
            color: "#333", // 🎨 COLOR SETTING: Title text color
            fontSize: "24px",
            fontWeight: "600"
          }}>
            {currentText.title}
          </h2>
        </div>

        {/* Username input field */}
        <input
          type="text"
          placeholder={currentText.usernamePlaceholder}
          value={username}
          onChange={(e) => setUsername(e.target.value)}
          style={{
            width: "100%",
            padding: "12px 16px",
            margin: "8px 0",
            borderRadius: "8px",
            border: "2px solid #e0e0e0", // 🎨 COLOR SETTING: Input border normal
            fontSize: "16px",
            outline: "none",
            transition: "border-color 0.3s",
            boxSizing: "border-box",
          }}
          onFocus={(e) => e.target.style.borderColor = "#4CAF50"} // 🎨 COLOR SETTING: Input border focus
          onBlur={(e) => e.target.style.borderColor = "#e0e0e0"} // 🎨 COLOR SETTING: Input border blur
        />

        {/* Password input field with eye toggle button */}
        <div style={{ 
          display: "flex", 
          width: "100%", 
          position: "relative",
          margin: "8px 0"
        }}>
          <input
            type={showPassword ? "text" : "password"}
            placeholder={currentText.passwordPlaceholder}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            style={{
              width: "100%",
              padding: "12px 50px 12px 16px",
              borderRadius: "8px",
              border: "2px solid #e0e0e0", // 🎨 COLOR SETTING: Password input border normal
              fontSize: "16px",
              outline: "none",
              transition: "border-color 0.3s",
              boxSizing: "border-box",
            }}
            onFocus={(e) => e.target.style.borderColor = "#4CAF50"} // 🎨 COLOR SETTING: Password input border focus
            onBlur={(e) => e.target.style.borderColor = "#e0e0e0"} // 🎨 COLOR SETTING: Password input border blur
          />
          
          {/* Eye icon button for password visibility toggle */}
          <button
            type="button"
            onClick={() => setShowPassword(!showPassword)}
            style={{
              position: "absolute",
              right: "12px",
              top: "50%",
              transform: "translateY(-50%)",
              padding: "8px",
              backgroundColor: "transparent",
              border: "none",
              cursor: "pointer",
              color: showPassword ? "#4CAF50" : "#666", // 🎨 COLOR SETTING: Eye icon color (active/inactive)
              borderRadius: "4px",
              display: "flex",
              alignItems: "center",
              justifyContent: "center"
            }}
            onMouseEnter={(e) => (e.target as HTMLButtonElement).style.backgroundColor = "#f0f0f0"} // 🎨 COLOR SETTING: Eye button hover
            onMouseLeave={(e) => (e.target as HTMLButtonElement).style.backgroundColor = "transparent"}
          >
            <EyeIcon isOpen={showPassword} />
          </button>
        </div>

        {/* Action buttons container */}
        <div style={{ 
          display: "flex", 
          gap: "12px", 
          marginTop: "25px",
          width: "100%"
        }}>
          {/* Login button */}
          <button
            onClick={handleLogin}
            style={{
              flex: 1,
              padding: "12px 20px",
              backgroundColor: "#4CAF50", // 🎨 COLOR SETTING: Login button background
              color: "white", // 🎨 COLOR SETTING: Login button text
              border: "none",
              borderRadius: "8px",
              cursor: "pointer",
              fontSize: "16px",
              fontWeight: "600",
              transition: "background-color 0.3s",
            }}
            onMouseEnter={(e) => (e.target as HTMLButtonElement).style.backgroundColor = "#45a049"} // 🎨 COLOR SETTING: Login button hover
            onMouseLeave={(e) => (e.target as HTMLButtonElement).style.backgroundColor = "#4CAF50"} // 🎨 COLOR SETTING: Login button normal
          >
            {currentText.loginButton}
          </button>
          
          {/* Reset button */}
          <button
            onClick={handleReset}
            style={{
              flex: 1,
              padding: "12px 20px",
              backgroundColor: "#f44336", // 🎨 COLOR SETTING: Reset button background
              color: "white", // 🎨 COLOR SETTING: Reset button text
              border: "none",
              borderRadius: "8px",
              cursor: "pointer",
              fontSize: "16px",
              fontWeight: "600",
              transition: "background-color 0.3s",
            }}
            onMouseEnter={(e) => (e.target as HTMLButtonElement).style.backgroundColor = "#da190b"} // 🎨 COLOR SETTING: Reset button hover
            onMouseLeave={(e) => (e.target as HTMLButtonElement).style.backgroundColor = "#f44336"} // 🎨 COLOR SETTING: Reset button normal
          >
            {currentText.resetButton}
          </button>
        </div>
      </div>
    </div>
  );
};

export default LoginBlock;