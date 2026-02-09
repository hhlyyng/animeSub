import { useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import DownloadIcon from "../../icon/DownloadIcon";
import PlayTriangleIcon from "../../icon/PlayTriangleIcon";
import { CloseIcon } from "../../icon/CloseIcon";

export type DownloadActionState = "idle" | "downloading" | "paused" | "completed";

type DownloadActionButtonProps = {
  state: DownloadActionState;
  progress: number;
  disabled?: boolean;
  onClick: () => void;
  secondaryAction?: {
    onClick: () => void;
    disabled?: boolean;
    ariaLabel?: string;
  };
};

const RADIUS = 12;
const STROKE_WIDTH = 2.4;
const SIZE = 40;
const CIRCUMFERENCE = 2 * Math.PI * RADIUS;
const HOVER_LOCK_MS = 300;

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

function clampProgress(progress: number): number {
  if (Number.isNaN(progress)) return 0;
  if (progress < 0) return 0;
  if (progress > 100) return 100;
  return progress;
}

export function DownloadActionButton({
  state,
  progress,
  disabled = false,
  onClick,
  secondaryAction,
}: DownloadActionButtonProps) {
  const [isHovered, setIsHovered] = useState(false);
  const [isHoverLocked, setIsHoverLocked] = useState(false);
  const hoverLockTimerRef = useRef<number | null>(null);
  const previousStateRef = useRef<DownloadActionState>(state);
  const isPointerInsideRef = useRef(false);
  const isIdle = state === "idle";
  const canShowSecondary = state === "paused" && Boolean(secondaryAction);
  const expandedWidth = SIZE * 2 + 6;
  const secondaryDisabled = secondaryAction?.disabled ?? false;
  const isStateChangingThisRender = previousStateRef.current !== state;

  useEffect(() => {
    return () => {
      if (hoverLockTimerRef.current !== null) {
        window.clearTimeout(hoverLockTimerRef.current);
        hoverLockTimerRef.current = null;
      }
    };
  }, []);

  const clearHoverLockTimer = () => {
    if (hoverLockTimerRef.current !== null) {
      window.clearTimeout(hoverLockTimerRef.current);
      hoverLockTimerRef.current = null;
    }
  };

  const handleMouseEnter = () => {
    isPointerInsideRef.current = true;
    if (!isHoverLocked) {
      setIsHovered(true);
    }
  };

  const handleMouseLeave = () => {
    isPointerInsideRef.current = false;
    setIsHovered(false);
  };

  useLayoutEffect(() => {
    const previous = previousStateRef.current;
    if (previous !== state) {
      previousStateRef.current = state;
      setIsHovered(false);
      setIsHoverLocked(true);
      clearHoverLockTimer();
      hoverLockTimerRef.current = window.setTimeout(() => {
        hoverLockTimerRef.current = null;
        setIsHoverLocked(false);
        if (isPointerInsideRef.current) {
          setIsHovered(true);
        }
      }, HOVER_LOCK_MS);
    }
  }, [state]);

  const isHoverEnabled = !isHoverLocked && !isStateChangingThisRender;
  const effectiveIsHovered = isHovered && isHoverEnabled;

  const iconHoverClass = !isHoverEnabled
    ? ""
    : isIdle
      ? "group-hover:scale-110 group-hover:text-gray-900"
      : "group-hover:scale-105";

  const visual = useMemo(() => {
    const normalizedProgress = clampProgress(progress);

    if (state === "completed") {
      return {
        ringColor: effectiveIsHovered ? "#dc2626" : "#16a34a",
        trackColor: effectiveIsHovered ? "rgba(220,38,38,0.15)" : "rgba(34,197,94,0.18)",
        progress: effectiveIsHovered ? 0 : 100,
        icon: effectiveIsHovered ? <CloseIcon className="block h-4 w-4" /> : <CheckIcon className="block h-4 w-4" />,
        iconColor: effectiveIsHovered ? "text-red-600" : "text-green-600",
      };
    }

    if (state === "downloading") {
      return {
        ringColor: "#16a34a",
        trackColor: "rgba(34,197,94,0.35)",
        progress: normalizedProgress,
        icon: <PauseIcon className="block h-4 w-4" />,
        iconColor: "text-green-700",
      };
    }

    if (state === "paused") {
      return {
        ringColor: "#4b5563",
        trackColor: "rgba(107,114,128,0.22)",
        progress: normalizedProgress,
        icon: <PlayTriangleIcon className="block h-3.5 w-3.5" />,
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
  }, [effectiveIsHovered, progress, state]);

  const dashOffset = CIRCUMFERENCE * (1 - visual.progress / 100);

  return (
    <div
        className="relative inline-flex items-center justify-end"
        onMouseEnter={handleMouseEnter}
        onMouseLeave={handleMouseLeave}
        style={{
          width: canShowSecondary ? (effectiveIsHovered ? expandedWidth : SIZE) : SIZE,
          height: SIZE,
          transition: canShowSecondary ? "width 220ms ease" : undefined,
        }}
      >
      {canShowSecondary && secondaryAction && (
        <button
          type="button"
          onClick={secondaryAction.onClick}
          disabled={secondaryDisabled}
          aria-label={secondaryAction.ariaLabel ?? "Remove torrent"}
          className={`group absolute left-0 inline-flex h-8 w-8 items-center justify-center ${
            secondaryDisabled ? "cursor-not-allowed opacity-60" : "cursor-pointer"
          }`}
          style={{
            appearance: "none",
            border: "none",
            background: "transparent",
            outline: "none",
            boxShadow: "none",
            padding: 0,
            margin: 0,
            opacity: effectiveIsHovered ? 1 : 0,
            transform: effectiveIsHovered ? "translateX(0)" : "translateX(-8px)",
            pointerEvents: effectiveIsHovered ? "auto" : "none",
            transition: "opacity 220ms ease, transform 220ms ease",
          }}
        >
          <CloseIcon className="h-4 w-4" />
        </button>
      )}

      <button
        type="button"
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
          className={`relative inline-flex h-8 w-8 items-center justify-center leading-none transition-all duration-200 ${visual.iconColor} ${iconHoverClass}`}
        >
          {visual.icon}
        </span>
      </button>
    </div>
  );
}

export default DownloadActionButton;
