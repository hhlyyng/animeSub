import { useState, useEffect } from "react";
import { useNavigate } from "react-router-dom";
import { useAppStore } from "../../../stores/useAppStores";
import * as authApi from "../../../services/authApi";

const LoginBlock = () => {
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  const [bgUrl, setBgUrl] = useState<string | null>(null);

  const language = useAppStore((state) => state.language);
  const setLanguage = useAppStore((state) => state.setLanguage);
  const setToken = useAppStore((state) => state.setToken);
  const setStoredUsername = useAppStore((state) => state.setUsername);
  const navigate = useNavigate();

  const zh = language === "zh";

  const text = {
    title: zh ? "动漫订阅" : "Anime Subscription",
    usernamePlaceholder: zh ? "请输入用户名" : "Enter Username",
    passwordPlaceholder: zh ? "请输入密码" : "Enter Password",
    loginButton: zh ? "登录" : "Login",
    loggingIn: zh ? "登录中..." : "Logging in...",
    emptyFields: zh ? "请输入用户名和密码" : "Please enter username and password",
  };

  useEffect(() => {
    const url = authApi.getLoginBackgroundUrl();
    const img = new Image();
    img.onload = () => setBgUrl(url);
    img.onerror = () => setBgUrl(null);
    img.src = url;
  }, []);

  const handleLogin = async () => {
    setError("");
    if (!username.trim() || !password.trim()) {
      setError(text.emptyFields);
      return;
    }

    setLoading(true);
    try {
      const result = await authApi.login({ username: username.trim(), password });
      setToken(result.token);
      setStoredUsername(result.username);
      navigate("/home", { replace: true });
    } catch (err) {
      setError(err instanceof Error ? err.message : "Login failed");
    } finally {
      setLoading(false);
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "Enter" && !loading) {
      void handleLogin();
    }
  };

  return (
    <div
      className="relative flex min-h-screen items-center justify-center p-5"
      style={
        bgUrl
          ? { backgroundImage: `url(${bgUrl})`, backgroundSize: "cover", backgroundPosition: "center" }
          : { backgroundColor: "#f9fafb" }
      }
    >
      {bgUrl && <div className="absolute inset-0 bg-black/30" />}

      <button
        onClick={() => setLanguage(zh ? "en" : "zh")}
        className="absolute right-5 top-5 z-20 rounded-full bg-gray-800 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-gray-700"
      >
        {zh ? "EN" : "中文"}
      </button>

      <div className="relative z-10 flex w-full max-w-[420px] flex-col items-center rounded-xl bg-white px-10 py-10 shadow-lg">
        <div className="mb-8 text-center">
          <div className="mx-auto mb-4 flex h-[60px] w-[60px] items-center justify-center rounded-full bg-gray-800 text-2xl font-bold text-white">
            A
          </div>
          <h2 className="m-0 text-2xl font-semibold text-gray-800">{text.title}</h2>
        </div>

        <input
          type="text"
          placeholder={text.usernamePlaceholder}
          value={username}
          onChange={(e) => setUsername(e.target.value)}
          onKeyDown={handleKeyDown}
          className="mb-3 h-12 w-full rounded-lg border-2 border-gray-200 px-4 text-base outline-none transition-colors focus:border-gray-500"
        />

        <div className="relative mb-3 w-full">
          <input
            type={showPassword ? "text" : "password"}
            placeholder={text.passwordPlaceholder}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            onKeyDown={handleKeyDown}
            className="h-12 w-full rounded-lg border-2 border-gray-200 px-4 pr-12 text-base outline-none transition-colors focus:border-gray-500"
          />
          <button
            type="button"
            onClick={() => setShowPassword(!showPassword)}
            className="absolute right-3 top-1/2 -translate-y-1/2 border-0 bg-transparent p-1 text-gray-500 hover:text-gray-800"
          >
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              {showPassword ? (
                <>
                  <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z" />
                  <circle cx="12" cy="12" r="3" />
                </>
              ) : (
                <>
                  <path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24" />
                  <line x1="1" y1="1" x2="23" y2="23" />
                </>
              )}
            </svg>
          </button>
        </div>

        {error && (
          <div className="mb-3 w-full text-center text-sm text-red-500">{error}</div>
        )}

        <button
          onClick={() => void handleLogin()}
          disabled={loading}
          className="mt-3 h-12 w-full rounded-lg bg-gray-800 text-base font-semibold text-white transition-colors hover:bg-gray-700 disabled:cursor-not-allowed disabled:opacity-60"
        >
          {loading ? text.loggingIn : text.loginButton}
        </button>
      </div>
    </div>
  );
};

export default LoginBlock;
