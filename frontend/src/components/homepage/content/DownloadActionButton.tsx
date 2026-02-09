import { useMemo, useState } from "react";
import DownloadIcon from "../../icon/DownloadIcon";

export type DownloadActionState = "idle" | "downloading" | "paused" | "completed";

type DownloadActionButtonProps = {
  state: DownloadActionState;
  progress: number;
  disabled?: boolean;
  onClick: () => void;
};

const RADIUS = 12;
const STROKE_WIDTH = 2.4;
const SIZE = 40;
const CIRCUMFERENCE = 2 * Math.PI * RADIUS;

function PauseIcon({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 24 24" fill="none" className={className} stroke="currentColor" strokeWidth={2.5}>
      <path d="M9 6v12" />
      <path d="M15 6v12" />
    </svg>
  );
}

function CheckIcon({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 24 24" fill="none" className={className} stroke="currentColor" strokeWidth={2.5}>
      <path d="M5 13l4 4L19 7" />
    </svg>
  );
}

function CloseIcon({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 24 24" fill="none" className={className} stroke="currentColor" strokeWidth={2.5}>
      <path d="M6 6l12 12" />
      <path d="M18 6l-12 12" />
    </svg>
  );
}

function clampProgress(progress: number): number {
  if (Number.isNaN(progress)) return 0;
  if (progress < 0) return 0;
  if (progress > 100) return 100;
  return progress;
}

export function DownloadActionButton({ state, progress, disabled = false, onClick }: DownloadActionButtonProps) {
  const [isHovered, setIsHovered] = useState(false);
  const isIdle = state === "idle";

  const visual = useMemo(() => {
    const normalizedProgress = clampProgress(progress);
    const visibleDownloadingProgress = normalizedProgress <= 0 ? 6 : normalizedProgress;

    if (state === "completed") {
      return {
        ringColor: isHovered ? "#dc2626" : "#16a34a",
        trackColor: isHovered ? "rgba(220,38,38,0.15)" : "rgba(34,197,94,0.18)",
        progress: isHovered ? 0 : 100,
        icon: isHovered ? <CloseIcon className="block h-4 w-4" /> : <CheckIcon className="block h-4 w-4" />,
        iconColor: isHovered ? "text-red-600" : "text-green-600",
      };
    }

    if (state === "downloading") {
      return {
        ringColor: "#16a34a",
        trackColor: "rgba(34,197,94,0.35)",
        progress: visibleDownloadingProgress,
        icon: <PauseIcon className="block h-4 w-4" />,
        iconColor: "text-green-700",
      };
    }

    if (state === "paused") {
      return {
        ringColor: "#4b5563",
        trackColor: "rgba(107,114,128,0.22)",
        progress: normalizedProgress,
        icon: <PauseIcon className="block h-4 w-4" />,
        iconColor: "text-gray-700",
      };
    }

    return {
      ringColor: "#9ca3af",
      trackColor: "transparent",
      progress: 0,
      icon: <DownloadIcon className="block h-4 w-4" />,
      iconColor: "text-gray-600",
    };
  }, [isHovered, progress, state]);

  const dashOffset = CIRCUMFERENCE * (1 - visual.progress / 100);

  return (
    <button
      type="button"
      onMouseEnter={() => setIsHovered(true)}
      onMouseLeave={() => setIsHovered(false)}
      onClick={onClick}
      disabled={disabled}
      className={`group relative ${disabled ? "opacity-60 cursor-not-allowed" : "cursor-pointer"}`}
      style={{
        appearance: "none",
        border: "none",
        background: "transparent",
        outline: "none",
        boxShadow: "none",
        padding: 0,
        margin: 0,
        position: "relative",
        width: SIZE,
        height: SIZE,
        display: "inline-flex",
        alignItems: "center",
        justifyContent: "center",
      }}
    >
      {!isIdle && (
        <svg
          className="pointer-events-none absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 -rotate-90"
          width={SIZE}
          height={SIZE}
          viewBox={`0 0 ${SIZE} ${SIZE}`}
          aria-hidden="true"
        >
          <circle
            cx={SIZE / 2}
            cy={SIZE / 2}
            r={RADIUS}
            stroke={visual.trackColor}
            strokeWidth={STROKE_WIDTH}
            fill="transparent"
          />
          <circle
            cx={SIZE / 2}
            cy={SIZE / 2}
            r={RADIUS}
            stroke={visual.ringColor}
            strokeWidth={STROKE_WIDTH}
            fill="transparent"
            strokeLinecap="round"
            strokeDasharray={CIRCUMFERENCE}
            strokeDashoffset={dashOffset}
            style={{
              transition: "stroke-dashoffset 320ms ease, stroke 220ms ease",
            }}
          />
        </svg>
      )}

      <span
        className={`relative inline-flex h-8 w-8 items-center justify-center leading-none transition-all duration-200 ${visual.iconColor} ${
          isIdle
            ? "group-hover:scale-110 group-hover:text-gray-900"
            : "group-hover:scale-105"
        }`}
      >
        {visual.icon}
      </span>
    </button>
  );
}

export default DownloadActionButton;
