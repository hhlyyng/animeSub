import { useState, useEffect } from "react";
import { useNavigate } from "react-router-dom";
import { useAppStore } from "../../../stores/useAppStores";
import * as authApi from "../../../services/authApi";
import { EyeOpenIcon } from "../../../components/icons/EyeOpenIcon";
import { EyeClosedIcon } from "../../../components/icons/EyeClosedIcon";

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
    title: "Anime-Sub",
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
      className="fixed inset-0 z-50 flex h-screen w-screen items-center justify-center p-5"
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
        <h2 className="mb-8 text-2xl font-bold text-gray-900">{text.title}</h2>

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
            className="group absolute right-2 top-1/2 -translate-y-1/2 !rounded-none !border-0 !bg-transparent !p-1 text-gray-600 [appearance:none] hover:!border-0 hover:!bg-transparent hover:text-gray-900 focus:!outline-none focus-visible:!outline-none"
          >
            {showPassword ? (
              <>
                <EyeOpenIcon className="h-4 w-4 group-hover:hidden" />
                <EyeClosedIcon className="hidden h-4 w-4 group-hover:block" />
              </>
            ) : (
              <>
                <EyeClosedIcon className="h-4 w-4 group-hover:hidden" />
                <EyeOpenIcon className="hidden h-4 w-4 group-hover:block" />
              </>
            )}
          </button>
        </div>

        {error && (
          <div className="mb-3 w-full text-center text-sm text-red-500">{error}</div>
        )}

        <button
          onClick={() => void handleLogin()}
          disabled={loading}
          className="mt-3 h-12 w-full rounded-lg border border-gray-300 bg-white text-base font-medium text-gray-900 transition-colors hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-60"
        >
          {loading ? text.loggingIn : text.loginButton}
        </button>
      </div>
    </div>
  );
};

export default LoginBlock;
