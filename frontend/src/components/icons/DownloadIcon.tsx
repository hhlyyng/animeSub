type DownloadIconProps = {
  className?: string;
};

const DownloadIcon = ({ className }: DownloadIconProps) => {
  const hasClass = typeof className === "string" && className.trim().length > 0;

  return (
    <svg
      width={hasClass ? undefined : "18"}
      height={hasClass ? undefined : "18"}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2.5"
      className={className}
    >
      <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
      <polyline points="7,10 12,15 17,10" />
      <line x1="12" y1="15" x2="12" y2="3" />
    </svg>
  );
};

export default DownloadIcon;
