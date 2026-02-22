import { useEffect, useMemo, useRef, useState } from "react";
import toast from "react-hot-toast";
import { EyeClosedIcon } from "../../../components/icons/EyeClosedIcon";
import { EyeOpenIcon } from "../../../components/icons/EyeOpenIcon";
import * as settingsApi from "../../../services/settingsApi";
import * as authApi from "../../../services/authApi";
import { useAppStore } from "../../../stores/useAppStores";
import type {
  DownloadPreferenceField,
  SettingsProfile,
  TestQbittorrentRequest,
} from "../../../types/settings";

type TestKey = "tmdbToken" | "host" | "port" | "username" | "password" | "defaultSavePath";
type TestState = "idle" | "testing" | "passed" | "failed";

type BaselineSnapshot = {
  appUsername: string;
  appPasswordConfigured: boolean;
  host: string;
  port: string;
  username: string;
  defaultSavePath: string;
  category: string;
  tags: string;
  pollingIntervalMinutes: string;
  subgroup: string;
  resolution: string;
  subtitleType: string;
  priorityOrder: DownloadPreferenceField[];
  passwordConfigured: boolean;
};

type TestResult = {
  ok: boolean;
  message: string;
  failures?: string[];
};

const DEFAULT_SUBGROUP = "all";
const DEFAULT_RESOLUTION = "1080P";
const DEFAULT_SUBTITLE_TYPE = "简日内嵌";
const DEFAULT_PRIORITY_ORDER: DownloadPreferenceField[] = ["subgroup", "resolution", "subtitleType"];
const REQUIRED_QB_TEST_KEYS: Array<Extract<TestKey, "host" | "port" | "username" | "password" | "defaultSavePath">> = [
  "host",
  "port",
  "username",
  "password",
  "defaultSavePath",
];

const INITIAL_TEST_STATES: Record<TestKey, TestState> = {
  tmdbToken: "idle",
  host: "idle",
  port: "idle",
  username: "idle",
  password: "idle",
  defaultSavePath: "idle",
};

function normalizePriorityOrder(input?: DownloadPreferenceField[]): DownloadPreferenceField[] {
  if (!input || input.length !== 3) return [...DEFAULT_PRIORITY_ORDER];
  const unique = Array.from(new Set(input));
  if (unique.length !== 3) return [...DEFAULT_PRIORITY_ORDER];
  if (!DEFAULT_PRIORITY_ORDER.every((field) => unique.includes(field))) return [...DEFAULT_PRIORITY_ORDER];
  return unique;
}

function ensureOptions(values: string[], requiredValue: string, currentValue: string): string[] {
  const sorted = values
    .map((value) => value.trim())
    .filter((value) => value.length > 0)
    .filter((value, index, arr) => arr.findIndex((target) => target.toLowerCase() === value.toLowerCase()) === index)
    .sort((a, b) => a.localeCompare(b, undefined, { sensitivity: "base" }));

  const pushIfMissing = (value: string, prepend = false) => {
    const index = sorted.findIndex((item) => item.toLowerCase() === value.toLowerCase());
    if (index >= 0) {
      if (prepend && index > 0) {
        const keep = sorted[index];
        sorted.splice(index, 1);
        sorted.unshift(keep);
      }
      return;
    }
    if (prepend) sorted.unshift(value);
    else sorted.push(value);
  };

  pushIfMissing(requiredValue, true);
  pushIfMissing(currentValue, false);
  return sorted;
}

function reorder(
  order: DownloadPreferenceField[],
  source: DownloadPreferenceField,
  target: DownloadPreferenceField
): DownloadPreferenceField[] {
  if (source === target) return order;
  const sourceIndex = order.indexOf(source);
  const targetIndex = order.indexOf(target);
  if (sourceIndex < 0 || targetIndex < 0) return order;
  const next = [...order];
  next.splice(sourceIndex, 1);
  next.splice(targetIndex, 0, source);
  return next;
}

function normalizePortText(value: string): string {
  const trimmed = value.trim();
  const parsed = Number.parseInt(trimmed, 10);
  if (!Number.isFinite(parsed)) return trimmed;
  return String(parsed);
}

function normalizePollingText(value: string): string {
  const trimmed = value.trim();
  if (!trimmed) return "";
  const parsed = Number.parseInt(trimmed, 10);
  if (!Number.isFinite(parsed)) return "invalid";
  return String(parsed);
}

function sameOrder(a: DownloadPreferenceField[], b: DownloadPreferenceField[]): boolean {
  return a.length === b.length && a.every((value, index) => value === b[index]);
}

function StatusBadge({
  state,
  passed,
  testing,
  failed,
}: {
  state: TestState;
  passed: string;
  testing: string;
  failed: string;
}) {
  if (state === "passed") return <span className="text-xs font-semibold text-green-600">{passed}</span>;
  if (state === "testing") return <span className="text-xs font-semibold text-blue-600">{testing}</span>;
  if (state === "failed") return <span className="text-xs font-semibold text-red-600">{failed}</span>;
  return null;
}

function HelpTip({ text }: { text: string }) {
  return (
    <span className="group relative inline-flex items-center">
      <span className="inline-flex h-3.5 w-3.5 items-center justify-center rounded-full border border-gray-400 text-[9px] leading-none text-gray-600">
        ?
      </span>
      <span className="pointer-events-none absolute left-0 top-full z-10 mt-1 w-96 rounded-md border border-gray-200 bg-white px-3 py-2 text-xs leading-relaxed text-gray-700 opacity-0 shadow-md transition-opacity group-hover:opacity-100">
        {text}
      </span>
    </span>
  );
}

function EyeToggleButton({
  visible,
  onToggle,
  showLabel,
  hideLabel,
}: {
  visible: boolean;
  onToggle: () => void;
  showLabel: string;
  hideLabel: string;
}) {
  return (
    <button
      type="button"
      aria-label={visible ? hideLabel : showLabel}
      title={visible ? hideLabel : showLabel}
      className="group absolute right-2 top-1/2 -translate-y-1/2 !rounded-none !border-0 !bg-transparent !p-1 text-gray-600 [appearance:none] hover:!border-0 hover:!bg-transparent hover:text-gray-900 focus:!outline-none focus-visible:!outline-none"
      onClick={onToggle}
    >
      {visible ? (
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
  );
}

function FieldLabel({
  title,
  required,
  helpText,
}: {
  title: string;
  required?: boolean;
  helpText?: string;
}) {
  return (
    <div className="leading-tight">
      <div className="inline-flex items-start gap-1">
        <div className="text-base font-normal text-gray-900">{title}</div>
        <div className="-mt-1 inline-flex items-start gap-0.5">
          {required ? <span className="text-[13px] leading-none text-red-300">*</span> : null}
          {helpText ? <HelpTip text={helpText} /> : null}
        </div>
      </div>
    </div>
  );
}

export default function SettingPage() {
  const language = useAppStore((state) => state.language);
  const loginUsername = useAppStore((state) => state.username);
  const randomFeedEnabled = useAppStore((state) => state.randomFeedEnabled);
  const randomFeedCount = useAppStore((state) => state.randomFeedCount);
  const setRandomFeedEnabled = useAppStore((state) => state.setRandomFeedEnabled);
  const setRandomFeedCount = useAppStore((state) => state.setRandomFeedCount);
  const zh = language === "zh";

  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [qbTesting, setQbTesting] = useState(false);
  const [batchTesting, setBatchTesting] = useState(false);
  const [profile, setProfile] = useState<SettingsProfile | null>(null);
  const [baseline, setBaseline] = useState<BaselineSnapshot | null>(null);

  const [appUsername, setAppUsername] = useState("");

  // Credentials change (login password + username)
  const [currentLoginPassword, setCurrentLoginPassword] = useState("");
  const [newLoginPassword, setNewLoginPassword] = useState("");
  const [confirmLoginPassword, setConfirmLoginPassword] = useState("");
  const [showCurrentLoginPw, setShowCurrentLoginPw] = useState(false);
  const [showNewLoginPw, setShowNewLoginPw] = useState(false);
  const [showConfirmLoginPw, setShowConfirmLoginPw] = useState(false);
  const [changingCredentials, setChangingCredentials] = useState(false);
  const [tmdbToken, setTmdbToken] = useState("");
  const [showTmdbToken, setShowTmdbToken] = useState(false);
  const [host, setHost] = useState("");
  const [port, setPort] = useState("8080");
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [defaultSavePath, setDefaultSavePath] = useState("");
  const [category, setCategory] = useState("anime");
  const [tags, setTags] = useState("AnimeSub");
  const [pollingIntervalMinutes, setPollingIntervalMinutes] = useState("");

  const [subgroupPreference, setSubgroupPreference] = useState(DEFAULT_SUBGROUP);
  const [resolutionPreference, setResolutionPreference] = useState(DEFAULT_RESOLUTION);
  const [subtitleTypePreference, setSubtitleTypePreference] = useState(DEFAULT_SUBTITLE_TYPE);
  const [priorityOrder, setPriorityOrder] = useState<DownloadPreferenceField[]>([...DEFAULT_PRIORITY_ORDER]);
  const [draggingField, setDraggingField] = useState<DownloadPreferenceField | null>(null);

  const [testStates, setTestStates] = useState<Record<TestKey, TestState>>(INITIAL_TEST_STATES);
  const [testMessages, setTestMessages] = useState<Partial<Record<TestKey, string>>>({});
  const [qbSummaryMessage, setQbSummaryMessage] = useState("");
  const [testSummaryMessage, setTestSummaryMessage] = useState("");

  const [bgPreviewUrl, setBgPreviewUrl] = useState<string | null>(null);
  const [bgUploading, setBgUploading] = useState(false);
  const bgFileInputRef = useRef<HTMLInputElement>(null);

  const text = {
    title: zh ? "设置" : "Settings",
    loading: zh ? "正在加载设置..." : "Loading settings...",
    required: zh ? "必填" : "Required",
    optional: zh ? "选填" : "Optional",
    passed: zh ? "通过" : "Passed",
    testing: zh ? "测试中..." : "Testing...",
    failed: zh ? "失败" : "Failed",
    test: zh ? "测试" : "Test",
    save: zh ? "保存设置" : "Save Settings",
    saving: zh ? "保存中..." : "Saving...",
    show: zh ? "显示" : "Show",
    hide: zh ? "隐藏" : "Hide",

    sectionAnimeSub: "AnimeSub",
    sectionTmdb: "TMDB Token",
    sectionQb: "qBittorrent",
    sectionMikan: "Mikan",
    sectionPreference: zh ? "下载偏好" : "Download Preferences",

    animeSubLabel: zh ? "用户名" : "Username",
    animeSubHelp: zh
      ? "修改用户名或密码后需要重新登录。"
      : "You will be redirected to login after changing username or password.",

    currentPasswordLabel: zh ? "当前密码" : "Current Password",
    newPasswordLabel: zh ? "新密码" : "New Password",
    confirmPasswordLabel: zh ? "确认新密码" : "Confirm New Password",
    changeCredentialsBtn: zh ? "保存修改" : "Save Changes",
    changingCredentialsBtn: zh ? "保存中..." : "Saving...",
    currentPasswordPlaceholder: zh ? "请输入当前登录密码" : "Enter current login password",
    newPasswordPlaceholder: zh ? "请输入新密码（至少4位）" : "New password (min 4 chars)",
    confirmPasswordPlaceholder: zh ? "再次输入新密码" : "Confirm new password",
    passwordMismatch: zh ? "两次输入的新密码不一致" : "New passwords do not match",

    tmdbHelp: zh
      ? "TMDB Token 用于访问 TMDB API，获取封面、简介和元数据。"
      : "TMDB token is required to fetch metadata, covers, and details from TMDB API.",
    mikanHelp: zh
      ? "轮询分钟数控制后端 RSS 扫描频率。默认 30 分钟表示系统每 30 分钟检查一次已订阅动漫并尝试推送到 qB。间隔越短，更新发现更快但请求更频繁；间隔越长，负载更低但可能延迟发现更新。有效范围 1~1440 分钟。"
      : "Polling minutes controls backend RSS scan frequency. Default 30 means checking subscribed anime every 30 minutes and pushing matched updates to qB. Shorter intervals detect updates faster but increase requests; longer intervals reduce load but may delay discovery. Valid range is 1-1440 minutes.",

    prefHelp: zh
      ? "订阅时会优先按此处偏好匹配；若无完全命中，将按优先级选择最接近片源。"
      : "Subscription uses these source preferences. If exact match is unavailable, closest source is selected by priority.",
    priorityHelp: zh ? "可拖动排序，越靠前优先级越高。" : "Drag to reorder. Top row has highest priority.",

    categoryLabel: zh ? "分类" : "Category",
    tagsLabel: zh ? "标签" : "Tags",
    categoryHelp: zh
      ? "可选。给 qB 任务设置分类，便于筛选和规则处理。"
      : "Optional. Applied as qB category for filtering/rules.",
    tagsHelp: zh
      ? "可选。给 qB 任务设置标签，便于检索和批量处理。"
      : "Optional. Applied as qB tags for filtering/batch operations.",

    any: zh ? "不限" : "Any",
    noChanges: zh ? "没有可保存的变更" : "No changes to save",
    tmdbNeedTest: zh ? "TMDB 已修改，需先测试通过" : "TMDB changed and must pass test",
    qbNeedTest: zh ? "qB 关键配置已修改，需先测试通过" : "qB connection fields changed and must pass tests",
    noPendingTests: zh ? "当前没有需要测试的变更" : "No pending changes require testing",
    testsPassed: zh ? "所需测试已通过" : "Required tests passed",
    testsFailed: zh ? "测试未通过，请先修正后再保存" : "Some tests failed. Fix them before saving.",

    qbFillRequiredFirst: zh ? "请先补全 qB 测试所需字段" : "Complete qB required fields before testing",
    qbAllPass: zh ? "qB 必填项测试全部通过" : "All qB required field tests passed",
    qbHasFail: zh ? "qB 必填项仍有失败" : "Some qB required field tests failed",

    tmdbInputRequired: zh ? "请先输入 TMDB Token" : "Please enter TMDB token first",
    invalidPort: zh ? "Port 格式不正确" : "Invalid port",
    invalidPolling: zh ? "轮询分钟数必须在 1 到 1440 之间" : "Polling minutes must be between 1 and 1440",
    missingFieldsPrefix: zh ? "缺少字段" : "Missing fields",

    saveSuccess: zh ? "设置已保存" : "Settings saved",
    saveFailed: zh ? "保存设置失败" : "Failed to save settings",
    loadFailed: zh ? "加载设置失败" : "Failed to load settings",

    sectionBackground: zh ? "登录背景" : "Login Background",
    bgUpload: zh ? "上传图片" : "Upload Image",
    bgRemove: zh ? "删除背景" : "Remove",
    bgNone: zh ? "未设置自定义背景" : "No custom background",
    bgHelp: zh ? "支持 JPG/PNG/WebP，最大 5MB" : "JPG/PNG/WebP, max 5MB",
  };

  const setFieldTesting = (key: TestKey) => {
    setTestStates((prev) => ({ ...prev, [key]: "testing" }));
    setTestMessages((prev) => ({ ...prev, [key]: "" }));
  };

  const setFieldResult = (key: TestKey, success: boolean, message: string) => {
    setTestStates((prev) => ({ ...prev, [key]: success ? "passed" : "failed" }));
    setTestMessages((prev) => ({ ...prev, [key]: message }));
  };

  const loadProfile = async () => {
    setLoading(true);
    try {
      const data = await settingsApi.getSettingsProfile();
      setProfile(data);

      const normalizedOrder = normalizePriorityOrder(data.downloadPreferences.priorityOrder);

      setAppUsername(loginUsername || data.animeSub?.username || "");
      setTmdbToken("");
      setShowTmdbToken(false);
      setHost(data.qbittorrent.host || "");
      setPort(String(data.qbittorrent.port || 8080));
      setUsername(data.qbittorrent.username || "");
      setPassword("");
      setShowPassword(false);
      setDefaultSavePath(data.qbittorrent.defaultSavePath || "");
      setCategory(data.qbittorrent.category || "anime");
      setTags(data.qbittorrent.tags || "AnimeSub");
      setPollingIntervalMinutes(String(data.mikan.pollingIntervalMinutes || ""));
      setSubgroupPreference(data.downloadPreferences.subgroup || DEFAULT_SUBGROUP);
      setResolutionPreference(data.downloadPreferences.resolution || DEFAULT_RESOLUTION);
      setSubtitleTypePreference(data.downloadPreferences.subtitleType || DEFAULT_SUBTITLE_TYPE);
      setPriorityOrder(normalizedOrder);

      setBaseline({
        appUsername: (loginUsername || data.animeSub?.username || "").trim(),
        appPasswordConfigured: Boolean(data.animeSub?.passwordConfigured),
        host: (data.qbittorrent.host || "").trim(),
        port: String(data.qbittorrent.port || 8080),
        username: (data.qbittorrent.username || "").trim(),
        defaultSavePath: (data.qbittorrent.defaultSavePath || "").trim(),
        category: (data.qbittorrent.category || "anime").trim(),
        tags: (data.qbittorrent.tags || "AnimeSub").trim(),
        pollingIntervalMinutes: String(data.mikan.pollingIntervalMinutes || "").trim(),
        subgroup: (data.downloadPreferences.subgroup || DEFAULT_SUBGROUP).trim(),
        resolution: (data.downloadPreferences.resolution || DEFAULT_RESOLUTION).trim(),
        subtitleType: (data.downloadPreferences.subtitleType || DEFAULT_SUBTITLE_TYPE).trim(),
        priorityOrder: normalizedOrder,
        passwordConfigured: Boolean(data.qbittorrent.passwordConfigured),
      });

      setTestStates(INITIAL_TEST_STATES);
      setTestMessages({});
      setQbSummaryMessage("");
      setTestSummaryMessage("");

      // Check if login background exists
      const bgUrl = authApi.getLoginBackgroundUrl();
      const img = new Image();
      img.onload = () => setBgPreviewUrl(bgUrl + "?t=" + Date.now());
      img.onerror = () => setBgPreviewUrl(null);
      img.src = bgUrl;
    } catch (error) {
      toast.error(error instanceof Error ? error.message : text.loadFailed);
    } finally {
      setLoading(false);
    }
  };

  const handleChangeCredentials = async () => {
    if (!currentLoginPassword.trim()) {
      toast.error(zh ? "请输入当前密码" : "Please enter current password");
      return;
    }

    const hasNewPw = newLoginPassword.trim().length > 0;
    const usernameChanged = baseline ? appUsername.trim() !== baseline.appUsername : false;

    if (!hasNewPw && !usernameChanged) {
      toast.error(zh ? "没有需要修改的内容" : "Nothing to change");
      return;
    }
    if (hasNewPw && newLoginPassword.trim().length < 4) {
      toast.error(zh ? "新密码至少4位" : "New password must be at least 4 characters");
      return;
    }
    if (hasNewPw && newLoginPassword !== confirmLoginPassword) {
      toast.error(text.passwordMismatch);
      return;
    }

    try {
      setChangingCredentials(true);
      const token = useAppStore.getState().token;
      await authApi.changeCredentials(
        currentLoginPassword,
        token ?? "",
        hasNewPw ? newLoginPassword : undefined,
        usernameChanged ? appUsername.trim() : undefined,
      );
      toast.success(zh ? "修改成功，请重新登录" : "Updated, please log in again");
      // Force logout and redirect to login
      useAppStore.getState().logout();
      window.location.href = "/login";
    } catch (err) {
      toast.error(err instanceof Error ? err.message : (zh ? "修改失败" : "Failed to update credentials"));
    } finally {
      setChangingCredentials(false);
    }
  };

  useEffect(() => {
    void loadProfile();
  }, []);

  const subgroupOptions = useMemo(
    () => ensureOptions(profile?.downloadPreferenceOptions?.subgroups ?? [], DEFAULT_SUBGROUP, subgroupPreference),
    [profile?.downloadPreferenceOptions?.subgroups, subgroupPreference]
  );

  const resolutionOptions = useMemo(
    () => ensureOptions(profile?.downloadPreferenceOptions?.resolutions ?? [], DEFAULT_RESOLUTION, resolutionPreference),
    [profile?.downloadPreferenceOptions?.resolutions, resolutionPreference]
  );

  const subtitleOptions = useMemo(
    () => ensureOptions(profile?.downloadPreferenceOptions?.subtitleTypes ?? [], DEFAULT_SUBTITLE_TYPE, subtitleTypePreference),
    [profile?.downloadPreferenceOptions?.subtitleTypes, subtitleTypePreference]
  );

  const parsedPort = Number.parseInt(port, 10);
  const portValid = Number.isFinite(parsedPort) && parsedPort >= 1 && parsedPort <= 65535;

  const hasConfiguredPassword = Boolean(profile?.qbittorrent?.passwordConfigured || baseline?.passwordConfigured);
  const passwordAvailableForSave = password.trim().length > 0 || hasConfiguredPassword;

  const normalizedCurrentPolling = normalizePollingText(pollingIntervalMinutes);
  const parsedPolling =
    normalizedCurrentPolling === "" || normalizedCurrentPolling === "invalid"
      ? null
      : Number.parseInt(normalizedCurrentPolling, 10);

  const pollingValid =
    normalizedCurrentPolling === "" ||
    (normalizedCurrentPolling !== "invalid" && parsedPolling !== null && parsedPolling >= 1 && parsedPolling <= 1440);

  const tmdbChanged = tmdbToken.trim().length > 0;
  const hostChanged = baseline ? host.trim() !== baseline.host : false;
  const portChanged = baseline ? normalizePortText(port) !== normalizePortText(baseline.port) : false;
  const usernameChanged = baseline ? username.trim() !== baseline.username : false;
  const defaultSavePathChanged = baseline ? defaultSavePath.trim() !== baseline.defaultSavePath : false;
  const passwordChanged = password.trim().length > 0;
  const categoryChanged = baseline ? category.trim() !== baseline.category : false;
  const tagsChanged = baseline ? tags.trim() !== baseline.tags : false;
  const normalizedBaselinePolling = baseline ? normalizePollingText(baseline.pollingIntervalMinutes) : "";
  const pollingChanged = baseline ? normalizedCurrentPolling !== normalizedBaselinePolling : false;

  const preferenceChanged = baseline
    ? subgroupPreference.trim() !== baseline.subgroup ||
      resolutionPreference.trim() !== baseline.resolution ||
      subtitleTypePreference.trim() !== baseline.subtitleType ||
      !sameOrder(priorityOrder, baseline.priorityOrder)
    : false;

  const qbConnectionChanged = hostChanged || portChanged || usernameChanged || defaultSavePathChanged || passwordChanged;

  const hasAnyChange =
    tmdbChanged ||
    qbConnectionChanged ||
    categoryChanged ||
    tagsChanged ||
    pollingChanged ||
    preferenceChanged;

  const qbRequiredFilledForTest =
    host.trim().length > 0 &&
    portValid &&
    username.trim().length > 0 &&
    defaultSavePath.trim().length > 0 &&
    (password.trim().length > 0 || hasConfiguredPassword);

  const qbRequiredFilledForSave =
    host.trim().length > 0 &&
    portValid &&
    username.trim().length > 0 &&
    defaultSavePath.trim().length > 0 &&
    passwordAvailableForSave;

  const requiredQbTestsPassed = useMemo(
    () => REQUIRED_QB_TEST_KEYS.every((key) => testStates[key] === "passed"),
    [testStates]
  );

  const tmdbReady = !tmdbChanged || testStates.tmdbToken === "passed";
  const qbReady = !qbConnectionChanged || requiredQbTestsPassed;
  const hasPendingTests = tmdbChanged || qbConnectionChanged;

  const canSave =
    hasAnyChange &&
    qbRequiredFilledForSave &&
    pollingValid &&
    tmdbReady &&
    qbReady &&
    !loading &&
    !saving &&
    !qbTesting &&
    !batchTesting;

  const missingFieldsForSave = useMemo(() => {
    const fields: string[] = [];
    if (!host.trim()) fields.push("Host");
    if (!port.trim() || !portValid) fields.push("Port");
    if (!username.trim()) fields.push(zh ? "用户名" : "Username");
    if (!defaultSavePath.trim()) fields.push(zh ? "默认下载路径" : "Default Save Path");
    if (!passwordAvailableForSave) fields.push(zh ? "密码" : "Password");
    return fields;
  }, [defaultSavePath, host, passwordAvailableForSave, port, portValid, username, zh]);

  const saveHints = useMemo(() => {
    const hints: string[] = [];
    if (!hasAnyChange) hints.push(text.noChanges);
    if (missingFieldsForSave.length > 0) {
      hints.push(`${text.missingFieldsPrefix}: ${missingFieldsForSave.join(", ")}`);
    }
    if (!pollingValid) hints.push(text.invalidPolling);
    if (tmdbChanged && !tmdbReady) hints.push(text.tmdbNeedTest);
    if (qbConnectionChanged && !qbReady) hints.push(text.qbNeedTest);
    return hints;
  }, [hasAnyChange, missingFieldsForSave, pollingValid, qbConnectionChanged, qbReady, text, tmdbChanged, tmdbReady]);

  const buildQbTestRequest = (field: TestQbittorrentRequest["field"]): TestQbittorrentRequest => ({
    field,
    host: host.trim(),
    port: portValid ? parsedPort : 0,
    username: username.trim(),
    password: password.trim() || undefined,
    defaultSavePath: defaultSavePath.trim(),
    category: category.trim(),
    tags: tags.trim(),
  });

  const runTmdbTest = async (): Promise<TestResult> => {
    if (!tmdbToken.trim()) {
      setFieldResult("tmdbToken", false, text.tmdbInputRequired);
      return { ok: false, message: text.tmdbInputRequired };
    }

    setFieldTesting("tmdbToken");
    try {
      const result = await settingsApi.testTmdbToken(tmdbToken.trim());
      setFieldResult("tmdbToken", true, result.message);
      return { ok: true, message: result.message };
    } catch (error) {
      const message = error instanceof Error ? error.message : "TMDB test failed";
      setFieldResult("tmdbToken", false, message);
      return { ok: false, message };
    }
  };

  const runQbRequiredTests = async (): Promise<TestResult> => {
    if (!qbRequiredFilledForTest) {
      const missing = [
        !host.trim() ? "Host" : null,
        !port.trim() || !portValid ? "Port" : null,
        !username.trim() ? (zh ? "用户名" : "Username") : null,
        !defaultSavePath.trim() ? (zh ? "默认下载路径" : "Default Save Path") : null,
        !(password.trim().length > 0 || hasConfiguredPassword) ? (zh ? "密码" : "Password") : null,
      ].filter((value): value is string => Boolean(value));

      const message = `${text.qbFillRequiredFirst}: ${missing.join(", ")}`;
      setQbSummaryMessage(message);
      toast.error(message);
      return { ok: false, message };
    }

    setQbTesting(true);
    setQbSummaryMessage("");

    const fieldNameMap: Record<Exclude<TestKey, "tmdbToken">, string> = {
      host: "Host",
      port: "Port",
      username: zh ? "用户名" : "Username",
      password: zh ? "密码" : "Password",
      defaultSavePath: zh ? "默认下载路径" : "Default Save Path",
    };

    const results = await Promise.all(
      REQUIRED_QB_TEST_KEYS.map(async (key) => {
        const field = key as TestQbittorrentRequest["field"];
        setFieldTesting(key);
        try {
          const result = await settingsApi.testQbittorrentField(buildQbTestRequest(field));
          setFieldResult(key, true, result.message);
          return { key, ok: true, message: result.message };
        } catch (error) {
          const message = error instanceof Error ? error.message : "qB test failed";
          setFieldResult(key, false, message);
          return { key, ok: false, message };
        }
      })
    );

    setQbTesting(false);

    const failed = results.filter((item) => !item.ok);
    if (failed.length === 0) {
      setQbSummaryMessage(text.qbAllPass);
      toast.success(text.qbAllPass);
      return { ok: true, message: text.qbAllPass };
    }

    const detailedFailures = failed.map((item) => `${fieldNameMap[item.key]}: ${item.message}`);
    const message = `${text.qbHasFail} | ${detailedFailures.join(" | ")}`;
    setQbSummaryMessage(message);
    toast.error(text.qbHasFail);
    return { ok: false, message, failures: detailedFailures };
  };

  const runPendingTests = async () => {
    if (!hasPendingTests) {
      setTestSummaryMessage(text.noPendingTests);
      toast.success(text.noPendingTests);
      return;
    }

    setBatchTesting(true);
    setTestSummaryMessage("");

    const failures: string[] = [];

    if (tmdbChanged) {
      const tmdbResult = await runTmdbTest();
      if (!tmdbResult.ok) failures.push(`${text.sectionTmdb}: ${tmdbResult.message}`);
    }

    if (qbConnectionChanged) {
      const qbResult = await runQbRequiredTests();
      if (!qbResult.ok) {
        if (qbResult.failures && qbResult.failures.length > 0) {
          failures.push(...qbResult.failures.map((item) => `${text.sectionQb}: ${item}`));
        } else {
          failures.push(`${text.sectionQb}: ${qbResult.message}`);
        }
      }
    }

    if (failures.length === 0) {
      setTestSummaryMessage(text.testsPassed);
      toast.success(text.testsPassed);
    } else {
      setTestSummaryMessage(failures.join(" | "));
      toast.error(text.testsFailed);
    }

    setBatchTesting(false);
  };

  const saveSettings = async () => {
    if (!canSave) {
      toast.error(saveHints[0] ?? text.testsFailed);
      return;
    }
    if (!portValid) {
      toast.error(text.invalidPort);
      return;
    }
    if (!pollingValid) {
      toast.error(text.invalidPolling);
      return;
    }

    try {
      setSaving(true);
      await settingsApi.updateSettingsProfile({
        tmdbToken: tmdbToken.trim() || null,
        qbittorrent: {
          host: host.trim(),
          port: parsedPort,
          username: username.trim(),
          password: password.trim() ? password : null,
          defaultSavePath: defaultSavePath.trim(),
          category: category.trim() || null,
          tags: tags.trim() || null,
        },
        animeSub: {
          username: appUsername.trim(),
        },
        mikan: {
          pollingIntervalMinutes: parsedPolling,
        },
        downloadPreferences: {
          subgroup: subgroupPreference,
          resolution: resolutionPreference,
          subtitleType: subtitleTypePreference,
          priorityOrder,
        },
      });
      toast.success(text.saveSuccess);
      await loadProfile();
    } catch (error) {
      toast.error(error instanceof Error ? error.message : text.saveFailed);
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <div className="content-header w-full max-w-[1200px] text-black">
        <div className="text-base">{text.loading}</div>
      </div>
    );
  }

  return (
    <div className="content-header w-full max-w-[1200px] pb-8 text-black">
      <div className="mb-5">
        <h2 className="m-0 text-2xl font-bold text-gray-900">{text.title}</h2>
      </div>

      <div className="mb-5 rounded-xl border border-gray-200 bg-white p-5">
        <h2 className="mb-4 mt-0 text-2xl font-bold text-gray-900">{text.sectionAnimeSub}</h2>
        <p className="mb-4 text-sm text-gray-500">{text.animeSubHelp}</p>
        <div className="space-y-3">
          <div className="grid grid-cols-[300px_1fr] items-start gap-3">
            <FieldLabel title={text.animeSubLabel} />
            <input
              type="text"
              value={appUsername}
              onChange={(e) => setAppUsername(e.target.value)}
              className="h-10 rounded-md border border-gray-300 px-3 text-sm"
            />
          </div>
          <div className="grid grid-cols-[300px_1fr] items-start gap-3">
            <FieldLabel title={text.currentPasswordLabel} />
            <div className="relative">
              <input
                type={showCurrentLoginPw ? "text" : "password"}
                value={currentLoginPassword}
                onChange={(e) => setCurrentLoginPassword(e.target.value)}
                placeholder={text.currentPasswordPlaceholder}
                className="h-10 w-full rounded-md border border-gray-300 px-3 pr-12 text-sm"
              />
              <EyeToggleButton
                visible={showCurrentLoginPw}
                onToggle={() => setShowCurrentLoginPw((prev) => !prev)}
                showLabel={text.show}
                hideLabel={text.hide}
              />
            </div>
          </div>
          <div className="grid grid-cols-[300px_1fr] items-start gap-3">
            <FieldLabel title={text.newPasswordLabel} />
            <div className="relative">
              <input
                type={showNewLoginPw ? "text" : "password"}
                value={newLoginPassword}
                onChange={(e) => setNewLoginPassword(e.target.value)}
                placeholder={text.newPasswordPlaceholder}
                className="h-10 w-full rounded-md border border-gray-300 px-3 pr-12 text-sm"
              />
              <EyeToggleButton
                visible={showNewLoginPw}
                onToggle={() => setShowNewLoginPw((prev) => !prev)}
                showLabel={text.show}
                hideLabel={text.hide}
              />
            </div>
          </div>
          <div className="grid grid-cols-[300px_1fr] items-start gap-3">
            <FieldLabel title={text.confirmPasswordLabel} />
            <div className="relative">
              <input
                type={showConfirmLoginPw ? "text" : "password"}
                value={confirmLoginPassword}
                onChange={(e) => setConfirmLoginPassword(e.target.value)}
                placeholder={text.confirmPasswordPlaceholder}
                className="h-10 w-full rounded-md border border-gray-300 px-3 pr-12 text-sm"
              />
              <EyeToggleButton
                visible={showConfirmLoginPw}
                onToggle={() => setShowConfirmLoginPw((prev) => !prev)}
                showLabel={text.show}
                hideLabel={text.hide}
              />
            </div>
          </div>
          <div className="flex justify-end pt-1">
            <button
              type="button"
              onClick={() => void handleChangeCredentials()}
              disabled={changingCredentials || !currentLoginPassword.trim()}
              className="inline-flex h-10 items-center justify-center rounded-md border border-gray-900 bg-transparent px-4 text-sm font-medium text-gray-900 hover:bg-gray-100 disabled:cursor-not-allowed disabled:border-gray-300 disabled:text-gray-400"
            >
              {changingCredentials ? text.changingCredentialsBtn : text.changeCredentialsBtn}
            </button>
          </div>
        </div>
      </div>

      <div className="mb-5 rounded-xl border border-gray-200 bg-white p-5">
        <h2 className="mb-4 mt-0 text-2xl font-bold text-gray-900">
          {zh ? "随机推荐 Feed" : "Random Recommendation Feed"}
        </h2>

        <div className="flex items-center justify-between py-2">
          <div>
            <p className="text-base font-normal text-gray-900">
              {zh ? "启用随机推荐" : "Enable random recommendations"}
            </p>
            <p className="text-xs text-gray-500">
              {zh ? "在主页显示随机推荐的动画 Feed" : "Show a random anime feed on the homepage"}
            </p>
          </div>
            <button
              type="button"
              role="switch"
              aria-checked={randomFeedEnabled}
              onClick={() => setRandomFeedEnabled(!randomFeedEnabled)}
              className="relative flex flex-shrink-0 cursor-pointer items-center focus:outline-none"
              style={{
                width: "51px",
                height: "31px",
                borderRadius: "15.5px",
                padding: "2px",
                border: "none",
                boxSizing: "border-box",
                backgroundColor: randomFeedEnabled ? "#34C759" : "#AEAEB2",
                transition: "background-color 0.2s ease-in-out",
              }}
            >
              <span
                className="transition-transform duration-200 ease-in-out"
                style={{
                  width: "27px",
                  height: "27px",
                  borderRadius: "50%",
                  backgroundColor: "white",
                  flexShrink: 0,
                  transform: randomFeedEnabled ? "translateX(20px)" : "translateX(0)",
                  boxShadow: "0 3px 8px rgba(0,0,0,0.15), 0 3px 1px rgba(0,0,0,0.06)",
                }}
              />
            </button>
        </div>

        {randomFeedEnabled && (
          <div className="mt-3 border-t border-gray-100 pt-3">
            <div className="grid grid-cols-[300px_1fr] items-start gap-3">
              <FieldLabel
                title={zh ? "显示数量" : "Card count"}
                helpText={zh
                  ? "主页随机推荐 Feed 显示的卡片数量，范围 1–20。每次刷新页面随机重新抽取。"
                  : "Number of cards shown in the random feed on the homepage (1–20). Reshuffled on each page load."}
              />
              <div className="flex items-center gap-3">
                <input
                  type="number"
                  min={1}
                  max={20}
                  value={randomFeedCount}
                  onChange={(e) => setRandomFeedCount(Number(e.target.value))}
                  className="h-10 w-24 rounded-md border border-gray-300 px-3 text-center text-sm"
                />
                <span className="text-xs text-gray-500">
                  {zh ? "最多 20 个" : "Max 20"}
                </span>
              </div>
            </div>
          </div>
        )}
      </div>

      <div className="mb-5 rounded-xl border border-gray-200 bg-white p-5">
        <h2 className="mb-4 mt-0 text-2xl font-bold text-gray-900">{text.sectionBackground}</h2>
        <p className="mb-3 text-xs text-gray-500">{text.bgHelp}</p>
        <div className="flex items-center gap-4">
          {bgPreviewUrl ? (
            <img
              src={bgPreviewUrl}
              alt="Login background"
              className="h-20 w-32 rounded-md border border-gray-200 object-cover"
            />
          ) : (
            <div className="flex h-20 w-32 items-center justify-center rounded-md border border-dashed border-gray-300 text-xs text-gray-400">
              {text.bgNone}
            </div>
          )}
          <div className="flex flex-col gap-2">
            <input
              ref={bgFileInputRef}
              type="file"
              accept=".jpg,.jpeg,.png,.webp"
              className="hidden"
              onChange={async (e) => {
                const file = e.target.files?.[0];
                if (!file) return;
                const token = useAppStore.getState().token;
                if (!token) return;
                setBgUploading(true);
                try {
                  await authApi.uploadLoginBackground(file, token);
                  setBgPreviewUrl(authApi.getLoginBackgroundUrl() + "?t=" + Date.now());
                  toast.success(zh ? "上传成功" : "Upload successful");
                } catch (err) {
                  toast.error(err instanceof Error ? err.message : "Upload failed");
                } finally {
                  setBgUploading(false);
                  if (bgFileInputRef.current) bgFileInputRef.current.value = "";
                }
              }}
            />
            <button
              type="button"
              disabled={bgUploading}
              onClick={() => bgFileInputRef.current?.click()}
              className="inline-flex h-8 items-center justify-center rounded-md border border-gray-300 bg-gray-100 px-3 text-xs hover:bg-gray-200 disabled:opacity-50"
            >
              {bgUploading ? "..." : text.bgUpload}
            </button>
            {bgPreviewUrl && (
              <button
                type="button"
                disabled={bgUploading}
                onClick={async () => {
                  const token = useAppStore.getState().token;
                  if (!token) return;
                  try {
                    await authApi.deleteLoginBackground(token);
                    setBgPreviewUrl(null);
                    toast.success(zh ? "已删除" : "Removed");
                  } catch (err) {
                    toast.error(err instanceof Error ? err.message : "Delete failed");
                  }
                }}
                className="inline-flex h-8 items-center justify-center rounded-md border border-red-300 bg-white px-3 text-xs text-red-600 hover:bg-red-50 disabled:opacity-50"
              >
                {text.bgRemove}
              </button>
            )}
          </div>
        </div>
      </div>

      <div className="mb-5 rounded-xl border border-gray-200 bg-white p-5">
        <div className="mb-4 flex items-center justify-between">
          <h2 className="m-0 text-2xl font-bold text-gray-900">{text.sectionTmdb}</h2>
          <HelpTip text={text.tmdbHelp} />
        </div>

        <div className="grid grid-cols-[300px_1fr_auto_auto] items-start gap-3">
          <FieldLabel title="TMDB Token" required />
          <div className="relative">
            <input
              type={showTmdbToken ? "text" : "password"}
              value={tmdbToken}
              onChange={(event) => {
                setTmdbToken(event.target.value);
                setTestStates((prev) => ({ ...prev, tmdbToken: "idle" }));
                setTestSummaryMessage("");
              }}
              placeholder={zh ? "输入新 Token（留空保持当前）" : "Enter new token (blank keeps current)"}
              className="h-10 w-full rounded-md border border-gray-300 px-3 pr-12 text-sm"
            />
            <EyeToggleButton
              visible={showTmdbToken}
              onToggle={() => setShowTmdbToken((prev) => !prev)}
              showLabel={text.show}
              hideLabel={text.hide}
            />
          </div>
          <button
            type="button"
            onClick={() => void runTmdbTest()}
            className="inline-flex h-10 items-center justify-center rounded-md border border-gray-300 bg-gray-100 px-3 text-xs leading-none hover:bg-gray-200"
          >
            {text.test}
          </button>
          <StatusBadge
            state={testStates.tmdbToken}
            passed={text.passed}
            testing={text.testing}
            failed={text.failed}
          />
        </div>

        {profile?.tmdb?.configured ? (
          <div className="mt-2 text-xs text-gray-600">{`Configured: ${profile.tmdb.preview}`}</div>
        ) : null}
        {testMessages.tmdbToken ? <div className="mt-1 text-xs text-gray-600">{testMessages.tmdbToken}</div> : null}
      </div>

      <div className="mb-5 rounded-xl border border-gray-200 bg-white p-5">
        <h2 className="mb-4 mt-0 text-2xl font-bold text-gray-900">{text.sectionQb}</h2>

        <div className="space-y-3">
          <div className="grid grid-cols-[300px_1fr_auto] items-start gap-3">
            <FieldLabel title="Host / Port" required />
            <div className="grid grid-cols-[minmax(0,1fr)_140px] gap-3">
              <input
                type="text"
                value={host}
                onChange={(event) => {
                  setHost(event.target.value);
                  setTestStates((prev) => ({ ...prev, host: "idle" }));
                  setQbSummaryMessage("");
                  setTestSummaryMessage("");
                }}
                className="h-10 rounded-md border border-gray-300 px-3 text-sm"
              />
              <input
                type="number"
                value={port}
                onChange={(event) => {
                  setPort(event.target.value);
                  setTestStates((prev) => ({ ...prev, port: "idle" }));
                  setQbSummaryMessage("");
                  setTestSummaryMessage("");
                }}
                className="h-10 rounded-md border border-gray-300 px-3 text-sm"
              />
            </div>
            <div className="flex items-center justify-end gap-3 pt-2">
              <StatusBadge
                state={testStates.host}
                passed={text.passed}
                testing={text.testing}
                failed={text.failed}
              />
              <StatusBadge
                state={testStates.port}
                passed={text.passed}
                testing={text.testing}
                failed={text.failed}
              />
            </div>
          </div>

          <div className="grid grid-cols-[300px_1fr_auto] items-start gap-3">
            <FieldLabel title={zh ? "用户名" : "Username"} required />
            <input
              type="text"
              value={username}
              onChange={(event) => {
                setUsername(event.target.value);
                setTestStates((prev) => ({ ...prev, username: "idle" }));
                setQbSummaryMessage("");
                setTestSummaryMessage("");
              }}
              className="h-10 rounded-md border border-gray-300 px-3 text-sm"
            />
            <div className="flex justify-end pt-2">
              <StatusBadge
                state={testStates.username}
                passed={text.passed}
                testing={text.testing}
                failed={text.failed}
              />
            </div>
          </div>

          <div className="grid grid-cols-[300px_1fr_auto] items-start gap-3">
            <FieldLabel title={zh ? "密码" : "Password"} required />
            <div className="relative">
              <input
                type={showPassword ? "text" : "password"}
                value={password}
                onChange={(event) => {
                  setPassword(event.target.value);
                  setTestStates((prev) => ({ ...prev, password: "idle" }));
                  setQbSummaryMessage("");
                  setTestSummaryMessage("");
                }}
                placeholder={
                  profile?.qbittorrent?.passwordConfigured
                    ? zh
                      ? "已配置密码，留空保持当前"
                      : "Password configured; blank keeps current"
                    : zh
                      ? "请输入 qB 密码"
                      : "Enter qB password"
                }
                className="h-10 w-full rounded-md border border-gray-300 px-3 pr-12 text-sm"
              />
              <EyeToggleButton
                visible={showPassword}
                onToggle={() => setShowPassword((prev) => !prev)}
                showLabel={text.show}
                hideLabel={text.hide}
              />
            </div>
            <div className="flex justify-end pt-2">
              <StatusBadge
                state={testStates.password}
                passed={text.passed}
                testing={text.testing}
                failed={text.failed}
              />
            </div>
          </div>

          <div className="grid grid-cols-[300px_1fr_auto] items-start gap-3">
            <FieldLabel title={zh ? "默认下载路径" : "Default Save Path"} required />
            <input
              type="text"
              value={defaultSavePath}
              onChange={(event) => {
                setDefaultSavePath(event.target.value);
                setTestStates((prev) => ({ ...prev, defaultSavePath: "idle" }));
                setQbSummaryMessage("");
                setTestSummaryMessage("");
              }}
              className="h-10 rounded-md border border-gray-300 px-3 text-sm"
            />
            <div className="flex justify-end pt-2">
              <StatusBadge
                state={testStates.defaultSavePath}
                passed={text.passed}
                testing={text.testing}
                failed={text.failed}
              />
            </div>
          </div>

          <div className="grid grid-cols-[300px_1fr] items-start gap-3">
            <FieldLabel title={text.categoryLabel} helpText={text.categoryHelp} />
            <input
              type="text"
              value={category}
              onChange={(event) => {
                setCategory(event.target.value);
                setTestSummaryMessage("");
              }}
              className="h-10 rounded-md border border-gray-300 px-3 text-sm"
            />
          </div>

          <div className="grid grid-cols-[300px_1fr] items-start gap-3">
            <FieldLabel title={text.tagsLabel} helpText={text.tagsHelp} />
            <input
              type="text"
              value={tags}
              onChange={(event) => {
                setTags(event.target.value);
                setTestSummaryMessage("");
              }}
              className="h-10 rounded-md border border-gray-300 px-3 text-sm"
            />
          </div>

          <div className="grid grid-cols-[300px_1fr] gap-3">
            <div />
            <div>
              <div className="flex justify-end">
                <button
                  type="button"
                  disabled={qbTesting || batchTesting}
                  onClick={() => void runQbRequiredTests()}
                  className="inline-flex h-9 items-center justify-center rounded-md border border-gray-300 bg-gray-100 px-3 text-xs leading-none hover:bg-gray-200 disabled:cursor-not-allowed disabled:opacity-60"
                >
                  {qbTesting ? text.testing : text.test}
                </button>
              </div>
              {!qbRequiredFilledForTest ? (
                <div className="mt-1 text-right text-xs text-gray-500">{text.qbFillRequiredFirst}</div>
              ) : null}
              {qbSummaryMessage ? <div className="mt-1 text-right text-xs text-gray-600">{qbSummaryMessage}</div> : null}
            </div>
          </div>
        </div>
      </div>

      <div className="mb-5 rounded-xl border border-gray-200 bg-white p-5">
        <h2 className="mb-4 mt-0 text-2xl font-bold text-gray-900">{text.sectionMikan}</h2>
        <div className="grid grid-cols-[300px_1fr] items-start gap-3">
          <FieldLabel title={zh ? "轮询分钟数" : "Polling Minutes"} helpText={text.mikanHelp} />
          <input
            type="number"
            min={1}
            max={1440}
            value={pollingIntervalMinutes}
            onChange={(event) => {
              setPollingIntervalMinutes(event.target.value);
              setTestSummaryMessage("");
            }}
            placeholder={zh ? "留空则沿用当前值" : "Blank keeps current value"}
            className="h-10 rounded-md border border-gray-300 px-3 text-sm"
          />
        </div>
      </div>

      <div className="mb-5 rounded-xl border border-gray-200 bg-white p-5">
        <h2 className="mb-2 mt-0 text-2xl font-bold text-gray-900">{text.sectionPreference}</h2>
        <div className="mb-4 text-xs text-gray-600">{text.prefHelp}</div>

        <div className="space-y-3">
          <div className="grid grid-cols-[300px_1fr] items-start gap-3">
            <FieldLabel title={zh ? "字幕组" : "Subgroup"} required />
            <select
              value={subgroupPreference}
              onChange={(event) => {
                setSubgroupPreference(event.target.value);
                setTestSummaryMessage("");
              }}
              className="h-10 rounded-md border border-gray-300 px-3 text-sm"
            >
              {subgroupOptions.map((value) => (
                <option key={value} value={value}>
                  {value.toLowerCase() === DEFAULT_SUBGROUP ? text.any : value}
                </option>
              ))}
            </select>
          </div>

          <div className="grid grid-cols-[300px_1fr] items-start gap-3">
            <FieldLabel title={zh ? "分辨率" : "Resolution"} required />
            <select
              value={resolutionPreference}
              onChange={(event) => {
                setResolutionPreference(event.target.value);
                setTestSummaryMessage("");
              }}
              className="h-10 rounded-md border border-gray-300 px-3 text-sm"
            >
              {resolutionOptions.map((value) => (
                <option key={value} value={value}>
                  {value}
                </option>
              ))}
            </select>
          </div>

          <div className="grid grid-cols-[300px_1fr] items-start gap-3">
            <FieldLabel title={zh ? "字幕类型" : "Subtitle Type"} required />
            <select
              value={subtitleTypePreference}
              onChange={(event) => {
                setSubtitleTypePreference(event.target.value);
                setTestSummaryMessage("");
              }}
              className="h-10 rounded-md border border-gray-300 px-3 text-sm"
            >
              {subtitleOptions.map((value) => (
                <option key={value} value={value}>
                  {value}
                </option>
              ))}
            </select>
          </div>

          <div className="grid grid-cols-[300px_1fr] items-start gap-3">
            <FieldLabel title={zh ? "偏好优先级" : "Priority Order"} />
            <div>
              <div className="mb-2 text-xs text-gray-500">{text.priorityHelp}</div>
              <div className="space-y-2">
                {priorityOrder.map((field, index) => (
                  <div
                    key={field}
                    draggable
                    onDragStart={() => setDraggingField(field)}
                    onDragEnd={() => setDraggingField(null)}
                    onDragOver={(event) => event.preventDefault()}
                    onDrop={() => {
                      if (!draggingField) return;
                      setPriorityOrder((prev) => reorder(prev, draggingField, field));
                      setDraggingField(null);
                      setTestSummaryMessage("");
                    }}
                    className="flex cursor-grab items-center justify-between rounded-md border border-gray-300 bg-gray-50 px-3 py-2 text-sm active:cursor-grabbing"
                  >
                    <span className="inline-flex items-center gap-2">
                      <span className="inline-flex h-5 w-5 items-center justify-center rounded-full bg-gray-200 text-[11px] font-semibold text-gray-700">
                        {index + 1}
                      </span>
                      <span>
                        {field === "subgroup"
                          ? (zh ? "字幕组" : "Subgroup")
                          : field === "resolution"
                            ? (zh ? "分辨率" : "Resolution")
                            : (zh ? "字幕类型" : "Subtitle Type")}
                      </span>
                    </span>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </div>
      </div>

      <div className="flex items-start justify-end gap-3">
        <button
          type="button"
          disabled={!hasPendingTests || batchTesting || qbTesting || saving}
          onClick={() => void runPendingTests()}
          className="inline-flex h-10 items-center justify-center rounded-md border border-gray-900 bg-transparent px-4 text-sm font-medium text-gray-900 hover:bg-gray-100 disabled:cursor-not-allowed disabled:border-gray-300 disabled:text-gray-400"
        >
          {batchTesting ? text.testing : text.test}
        </button>

        <button
          type="button"
          disabled={!canSave}
          onClick={() => void saveSettings()}
          className="inline-flex h-10 items-center justify-center rounded-md border border-gray-900 bg-transparent px-4 text-sm font-medium text-gray-900 hover:bg-gray-100 disabled:cursor-not-allowed disabled:border-gray-300 disabled:text-gray-400"
        >
          {saving ? text.saving : text.save}
        </button>
      </div>

      {testSummaryMessage ? <div className="mt-2 text-right text-xs text-gray-700">{testSummaryMessage}</div> : null}

      {!canSave && saveHints.length > 0 ? (
        <div className="mt-2 space-y-1 text-right text-xs text-red-600">
          {saveHints.map((hint, index) => (
            <div key={`${hint}-${index}`}>{hint}</div>
          ))}
        </div>
      ) : null}
    </div>
  );
}
