import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { useAppStore } from "../../stores/useAppStores";
import * as authApi from "../../services/authApi";
import * as settingsApi from "../../services/settingsApi";
import type { DownloadPreferenceField } from "../../types/settings";
import LanguageToggleIcon from "../../components/icons/LanguageToggleIcon";
import { EyeOpenIcon } from "../../components/icons/EyeOpenIcon";
import { EyeClosedIcon } from "../../components/icons/EyeClosedIcon";

const STEPS = 5;
const RESOLUTIONS = ["1080P", "720P", "4K"];
const SUBTITLE_TYPES = ["简日内嵌", "繁日内嵌", "简日外挂", "繁日外挂"];
const DEFAULT_PRIORITY: DownloadPreferenceField[] = ["subgroup", "resolution", "subtitleType"];

type CheckStatus = "pending" | "checking" | "passed" | "failed";
type VerificationCheck = {
  label: string;
  status: CheckStatus;
  message?: string;
};

interface SetupPageProps {
  onSetupComplete?: () => void;
}

export default function SetupPage({ onSetupComplete }: SetupPageProps) {
  const language = useAppStore((state) => state.language);
  const setLanguage = useAppStore((state) => state.setLanguage);
  const navigate = useNavigate();
  const zh = language === "zh";

  const [step, setStep] = useState(0);
  const [submitting, setSubmitting] = useState(false);
  const [errorKey, setErrorKey] = useState<"" | "required" | "pwMismatch" | "setupFailed" | "custom">("");
  const [customError, setCustomError] = useState("");

  // Step 1 - Account
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [showConfirmPassword, setShowConfirmPassword] = useState(false);

  // Step 2 - qBittorrent
  const [qbHost, setQbHost] = useState("");
  const [qbPort, setQbPort] = useState("8080");
  const [qbUsername, setQbUsername] = useState("admin");
  const [qbPassword, setQbPassword] = useState("");
  const [qbSavePath, setQbSavePath] = useState("");
  const [qbCategory, setQbCategory] = useState("anime");
  const [qbTags, setQbTags] = useState("AnimeSub");

  // Step 3 - TMDB
  const [tmdbToken, setTmdbToken] = useState("");
  const [showTmdb, setShowTmdb] = useState(false);

  // Step 4 - Preferences
  const [resolution, setResolution] = useState("1080P");
  const [subtitleType, setSubtitleType] = useState("简日内嵌");
  const [subgroup, setSubgroup] = useState("all");
  const [priorityOrder, setPriorityOrder] = useState<DownloadPreferenceField[]>([...DEFAULT_PRIORITY]);
  const [dragging, setDragging] = useState<DownloadPreferenceField | null>(null);

  // Step 5 - Verification
  const [checks, setChecks] = useState<VerificationCheck[]>([]);
  const [verificationDone, setVerificationDone] = useState(false);
  const [allPassed, setAllPassed] = useState(false);

  const text = {
    title: zh ? "初始设置" : "Initial Setup",
    stepAccount: zh ? "创建账号" : "Create Account",
    stepQb: "qBittorrent",
    stepTmdb: "TMDB Token",
    stepPref: zh ? "下载偏好" : "Download Preferences",
    stepVerify: zh ? "验证 & 完成" : "Verify & Complete",
    next: zh ? "下一步" : "Next",
    back: zh ? "上一步" : "Back",
    complete: zh ? "完成设置" : "Complete Setup",
    retry: zh ? "重试" : "Retry",
    username: zh ? "用户名" : "Username",
    password: zh ? "密码" : "Password",
    confirmPw: zh ? "确认密码" : "Confirm Password",
    pwMismatch: zh ? "两次输入的密码不一致" : "Passwords do not match",
    required: zh ? "请填写所有必填项" : "Please fill all required fields",
    host: zh ? "主机地址" : "Host",
    port: zh ? "端口" : "Port",
    savePath: zh ? "默认下载路径" : "Default Save Path",
    category: zh ? "分类" : "Category",
    tags: zh ? "标签" : "Tags",
    tmdbHelp: zh
      ? "TMDB Token 用于获取动画封面和元数据。前往 themoviedb.org 注册获取。"
      : "TMDB Token is needed for anime covers and metadata. Register at themoviedb.org to get one.",
    subgroup: zh ? "字幕组" : "Subgroup",
    subgroupAll: zh ? "不限" : "Any",
    resLabel: zh ? "分辨率" : "Resolution",
    subType: zh ? "字幕类型" : "Subtitle Type",
    priority: zh ? "偏好优先级" : "Priority Order",
    priorityHelp: zh ? "拖拽排序，越靠前优先级越高" : "Drag to reorder. Top has highest priority.",
    verifyTmdb: zh ? "验证 TMDB Token" : "Verify TMDB Token",
    verifyQb: zh ? "验证 qBittorrent 连接" : "Verify qBittorrent Connection",
    verifyAccount: zh ? "验证账号信息" : "Verify Account Info",
    autoVerify: zh ? "正在自动验证..." : "Auto-verifying...",
    setupFailed: zh ? "设置失败" : "Setup failed",
  };

  const stepLabels = [text.stepAccount, text.stepQb, text.stepTmdb, text.stepPref, text.stepVerify];

  const parsedPort = Number.parseInt(qbPort, 10);
  const portValid = Number.isFinite(parsedPort) && parsedPort >= 1 && parsedPort <= 65535;

  const validateStep = (s: number): "" | "required" | "pwMismatch" | null => {
    if (s === 0) {
      if (!username.trim() || !password.trim() || !confirmPassword.trim()) return "required";
      if (password !== confirmPassword) return "pwMismatch";
    }
    if (s === 1) {
      if (!qbHost.trim() || !portValid || !qbUsername.trim() || !qbPassword.trim() || !qbSavePath.trim())
        return "required";
    }
    if (s === 2) {
      if (!tmdbToken.trim()) return "required";
    }
    return null;
  };

  const goNext = () => {
    const key = validateStep(step);
    if (key) {
      setErrorKey(key);
      return;
    }
    setErrorKey("");
    if (step === 3) {
      // Moving to step 5 (verify) — auto-start verification
      setStep(4);
      void runVerification();
    } else {
      setStep((s) => Math.min(s + 1, STEPS - 1));
    }
  };

  const goBack = () => {
    setErrorKey("");
    setStep((s) => Math.max(s - 1, 0));
  };

  const runVerification = async () => {
    setVerificationDone(false);
    setAllPassed(false);

    const newChecks: VerificationCheck[] = [
      { label: text.verifyTmdb, status: "pending" },
      { label: text.verifyQb, status: "pending" },
      { label: text.verifyAccount, status: "pending" },
    ];
    setChecks([...newChecks]);

    // Check TMDB
    newChecks[0].status = "checking";
    setChecks([...newChecks]);
    try {
      await settingsApi.testTmdbToken(tmdbToken.trim());
      newChecks[0].status = "passed";
      newChecks[0].message = zh ? "TMDB Token 有效" : "TMDB Token valid";
    } catch (err) {
      newChecks[0].status = "failed";
      newChecks[0].message = err instanceof Error ? err.message : "Failed";
    }
    setChecks([...newChecks]);

    // Check qBittorrent
    newChecks[1].status = "checking";
    setChecks([...newChecks]);
    try {
      await settingsApi.testQbittorrentField({
        field: "password",
        host: qbHost.trim(),
        port: parsedPort,
        username: qbUsername.trim(),
        password: qbPassword.trim(),
        defaultSavePath: qbSavePath.trim(),
      });
      newChecks[1].status = "passed";
      newChecks[1].message = zh ? "qBittorrent 连接成功" : "qBittorrent connected";
    } catch (err) {
      newChecks[1].status = "failed";
      newChecks[1].message = err instanceof Error ? err.message : "Failed";
    }
    setChecks([...newChecks]);

    // Check account
    newChecks[2].status = "checking";
    setChecks([...newChecks]);
    if (username.trim().length > 0 && password.length >= 1 && password === confirmPassword) {
      newChecks[2].status = "passed";
      newChecks[2].message = zh ? "账号信息有效" : "Account info valid";
    } else {
      newChecks[2].status = "failed";
      newChecks[2].message = zh ? "账号信息不完整" : "Account info incomplete";
    }
    setChecks([...newChecks]);

    const passed = newChecks.every((c) => c.status === "passed");
    setAllPassed(passed);
    setVerificationDone(true);
  };

  const completeSetup = async () => {
    setSubmitting(true);
    setErrorKey("");
    try {
      await authApi.setup({
        username: username.trim(),
        password,
        tmdbToken: tmdbToken.trim(),
        qbittorrent: {
          host: qbHost.trim(),
          port: parsedPort,
          username: qbUsername.trim(),
          password: qbPassword.trim(),
          defaultSavePath: qbSavePath.trim(),
          category: qbCategory.trim() || "anime",
          tags: qbTags.trim() || "AnimeSub",
        },
        downloadPreferences: {
          subgroup: subgroup.trim() || "all",
          resolution,
          subtitleType,
          priorityOrder,
        },
      });
      onSetupComplete?.();
      navigate("/login", { replace: true });
    } catch (err) {
      const msg = err instanceof Error ? err.message : "";
      if (msg) {
        setErrorKey("custom");
        setCustomError(msg);
      } else {
        setErrorKey("setupFailed");
      }
    } finally {
      setSubmitting(false);
    }
  };

  const reorder = (source: DownloadPreferenceField, target: DownloadPreferenceField) => {
    if (source === target) return;
    setPriorityOrder((prev) => {
      const next = [...prev];
      const si = next.indexOf(source);
      const ti = next.indexOf(target);
      if (si < 0 || ti < 0) return prev;
      next.splice(si, 1);
      next.splice(ti, 0, source);
      return next;
    });
  };

  const fieldLabel = (f: DownloadPreferenceField) => {
    if (f === "subgroup") return zh ? "字幕组" : "Subgroup";
    if (f === "resolution") return zh ? "分辨率" : "Resolution";
    return zh ? "字幕类型" : "Subtitle Type";
  };

  const StatusIcon = ({ status }: { status: CheckStatus }) => {
    if (status === "checking") return <span className="inline-block h-4 w-4 animate-spin rounded-full border-2 border-gray-300 border-t-gray-800" />;
    if (status === "passed") return <span className="text-green-600 font-bold">&#10003;</span>;
    if (status === "failed") return <span className="text-red-500 font-bold">&#10007;</span>;
    return <span className="text-gray-300">&#9679;</span>;
  };

  const inputClass = "h-10 w-full rounded-md border border-gray-300 px-3 text-sm outline-none focus:border-gray-500";

  return (
    <div className="fixed inset-0 z-50 flex h-screen w-screen items-center justify-center bg-gray-100 p-5">
      <button
        onClick={() => setLanguage(zh ? "en" : "zh")}
        className="absolute right-5 top-5 z-20 flex items-center gap-1.5 rounded-full border border-gray-300 bg-white px-3 py-2 text-sm font-medium text-gray-900 transition-colors hover:bg-gray-50"
      >
        <span className="h-5 w-5"><LanguageToggleIcon /></span>
        {zh ? "EN" : "中文"}
      </button>

      <div className="w-full max-w-lg rounded-xl bg-white p-8 shadow-lg">
        <h2 className="mb-1 text-center text-2xl font-bold text-gray-900">{text.title}</h2>

        {/* Step indicator */}
        <div className="mb-6 flex items-center justify-center gap-2 pt-2">
          {stepLabels.map((label, i) => (
            <div key={label} className="flex items-center gap-2">
              <div
                className={`flex h-7 w-7 items-center justify-center rounded-full text-xs font-semibold ${
                  i <= step ? "bg-gray-800 text-white" : "bg-gray-200 text-gray-500"
                }`}
              >
                {i + 1}
              </div>
              {i < STEPS - 1 && (
                <div className={`h-0.5 w-6 ${i < step ? "bg-gray-800" : "bg-gray-200"}`} />
              )}
            </div>
          ))}
        </div>
        <div className="mb-5 text-center text-sm font-medium text-gray-600">{stepLabels[step]}</div>

        {/* Step 1: Account */}
        {step === 0 && (
          <div className="space-y-4">
            <div>
              <label className="mb-1 block text-sm font-medium text-gray-700">{text.username} *</label>
              <input type="text" value={username} onChange={(e) => setUsername(e.target.value)} className={inputClass} />
            </div>
            <div>
              <label className="mb-1 block text-sm font-medium text-gray-700">{text.password} *</label>
              <div className="relative">
                <input
                  type={showPassword ? "text" : "password"}
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  className={inputClass + " pr-10"}
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
            </div>
            <div>
              <label className="mb-1 block text-sm font-medium text-gray-700">{text.confirmPw} *</label>
              <div className="relative">
                <input
                  type={showConfirmPassword ? "text" : "password"}
                  value={confirmPassword}
                  onChange={(e) => setConfirmPassword(e.target.value)}
                  className={inputClass + " pr-10"}
                />
                <button
                  type="button"
                  onClick={() => setShowConfirmPassword(!showConfirmPassword)}
                  className="group absolute right-2 top-1/2 -translate-y-1/2 !rounded-none !border-0 !bg-transparent !p-1 text-gray-600 [appearance:none] hover:!border-0 hover:!bg-transparent hover:text-gray-900 focus:!outline-none focus-visible:!outline-none"
                >
                  {showConfirmPassword ? (
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
            </div>
          </div>
        )}

        {/* Step 2: qBittorrent */}
        {step === 1 && (
          <div className="space-y-3">
            <div className="grid grid-cols-[1fr_120px] gap-3">
              <div>
                <label className="mb-1 block text-sm font-medium text-gray-700">{text.host} *</label>
                <input type="text" value={qbHost} onChange={(e) => setQbHost(e.target.value)} placeholder="192.168.1.100" className={inputClass} />
              </div>
              <div>
                <label className="mb-1 block text-sm font-medium text-gray-700">{text.port} *</label>
                <input type="number" value={qbPort} onChange={(e) => setQbPort(e.target.value)} className={inputClass} />
              </div>
            </div>
            <div>
              <label className="mb-1 block text-sm font-medium text-gray-700">{text.username} *</label>
              <input type="text" value={qbUsername} onChange={(e) => setQbUsername(e.target.value)} className={inputClass} />
            </div>
            <div>
              <label className="mb-1 block text-sm font-medium text-gray-700">{text.password} *</label>
              <input type="password" value={qbPassword} onChange={(e) => setQbPassword(e.target.value)} className={inputClass} />
            </div>
            <div>
              <label className="mb-1 block text-sm font-medium text-gray-700">{text.savePath} *</label>
              <input type="text" value={qbSavePath} onChange={(e) => setQbSavePath(e.target.value)} placeholder="/downloads/anime" className={inputClass} />
            </div>
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="mb-1 block text-sm font-medium text-gray-700">{text.category}</label>
                <input type="text" value={qbCategory} onChange={(e) => setQbCategory(e.target.value)} className={inputClass} />
              </div>
              <div>
                <label className="mb-1 block text-sm font-medium text-gray-700">{text.tags}</label>
                <input type="text" value={qbTags} onChange={(e) => setQbTags(e.target.value)} className={inputClass} />
              </div>
            </div>
          </div>
        )}

        {/* Step 3: TMDB */}
        {step === 2 && (
          <div className="space-y-4">
            <div>
              <label className="mb-1 block text-sm font-medium text-gray-700">TMDB Token *</label>
              <div className="relative">
                <input
                  type={showTmdb ? "text" : "password"}
                  value={tmdbToken}
                  onChange={(e) => setTmdbToken(e.target.value)}
                  className={inputClass + " pr-14"}
                />
                <button
                  type="button"
                  onClick={() => setShowTmdb(!showTmdb)}
                  className="group absolute right-2 top-1/2 -translate-y-1/2 !rounded-none !border-0 !bg-transparent !p-1 text-gray-600 [appearance:none] hover:!border-0 hover:!bg-transparent hover:text-gray-900 focus:!outline-none focus-visible:!outline-none"
                >
                  {showTmdb ? (
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
            </div>
            <p className="text-xs leading-relaxed text-gray-500">{text.tmdbHelp}</p>
          </div>
        )}

        {/* Step 4: Preferences */}
        {step === 3 && (
          <div className="space-y-4">
            <div>
              <label className="mb-1 block text-sm font-medium text-gray-700">{text.resLabel}</label>
              <select value={resolution} onChange={(e) => setResolution(e.target.value)} className={inputClass}>
                {RESOLUTIONS.map((r) => (
                  <option key={r} value={r}>{r}</option>
                ))}
              </select>
            </div>
            <div>
              <label className="mb-1 block text-sm font-medium text-gray-700">{text.subType}</label>
              <select value={subtitleType} onChange={(e) => setSubtitleType(e.target.value)} className={inputClass}>
                {SUBTITLE_TYPES.map((s) => (
                  <option key={s} value={s}>{s}</option>
                ))}
              </select>
            </div>
            <div>
              <label className="mb-1 block text-sm font-medium text-gray-700">{text.subgroup}</label>
              <input type="text" value={subgroup} onChange={(e) => setSubgroup(e.target.value)} placeholder={text.subgroupAll} className={inputClass} />
            </div>
            <div>
              <label className="mb-1 block text-sm font-medium text-gray-700">{text.priority}</label>
              <p className="mb-2 text-xs text-gray-500">{text.priorityHelp}</p>
              <div className="space-y-2">
                {priorityOrder.map((field, index) => (
                  <div
                    key={field}
                    draggable
                    onDragStart={() => setDragging(field)}
                    onDragEnd={() => setDragging(null)}
                    onDragOver={(e) => e.preventDefault()}
                    onDrop={() => {
                      if (dragging) reorder(dragging, field);
                      setDragging(null);
                    }}
                    className="flex cursor-grab items-center gap-2 rounded-md border border-gray-300 bg-gray-50 px-3 py-2 text-sm active:cursor-grabbing"
                  >
                    <span className="flex h-5 w-5 items-center justify-center rounded-full bg-gray-200 text-[11px] font-semibold text-gray-700">
                      {index + 1}
                    </span>
                    {fieldLabel(field)}
                  </div>
                ))}
              </div>
            </div>
          </div>
        )}

        {/* Step 5: Verification */}
        {step === 4 && (
          <div className="space-y-4">
            {checks.length === 0 && (
              <p className="text-center text-sm text-gray-500">{text.autoVerify}</p>
            )}
            {checks.map((check, i) => (
              <div key={i} className="flex items-center gap-3 rounded-md border border-gray-200 bg-gray-50 px-4 py-3">
                <StatusIcon status={check.status} />
                <div className="flex-1">
                  <div className="text-sm font-medium text-gray-800">{check.label}</div>
                  {check.message && (
                    <div className={`text-xs ${check.status === "failed" ? "text-red-500" : "text-gray-500"}`}>
                      {check.message}
                    </div>
                  )}
                </div>
              </div>
            ))}
          </div>
        )}

        {/* Error */}
        {errorKey && <div className="mt-3 text-center text-sm text-red-500">{errorKey === "custom" ? customError : text[errorKey]}</div>}

        {/* Navigation */}
        <div className="mt-6 flex justify-between gap-3">
          {step > 0 ? (
            <button
              type="button"
              onClick={goBack}
              disabled={submitting}
              className="h-10 rounded-md border border-gray-300 bg-white px-5 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
            >
              {text.back}
            </button>
          ) : (
            <div />
          )}

          {step < 4 && (
            <button
              type="button"
              onClick={(e) => { (e.currentTarget as HTMLButtonElement).blur(); goNext(); }}
              className="h-10 rounded-md border border-gray-300 bg-white px-5 text-sm font-medium text-gray-900 hover:bg-gray-50 focus:outline-none"
            >
              {text.next}
            </button>
          )}

          {step === 4 && verificationDone && !allPassed && (
            <button
              type="button"
              onClick={() => void runVerification()}
              className="h-10 rounded-md border border-gray-800 bg-white px-5 text-sm font-medium text-gray-800 hover:bg-gray-50"
            >
              {text.retry}
            </button>
          )}

          {step === 4 && allPassed && (
            <button
              type="button"
              onClick={() => void completeSetup()}
              disabled={submitting}
              className="h-10 rounded-md border border-gray-300 bg-white px-5 text-sm font-medium text-gray-900 hover:bg-gray-50 disabled:opacity-50"
            >
              {submitting ? "..." : text.complete}
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
