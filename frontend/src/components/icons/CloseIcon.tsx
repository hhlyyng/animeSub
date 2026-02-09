type CloseIconProps = {
  className?: string;
};

export function CloseIcon({ className = "w-5 h-5" }: CloseIconProps) {
  return (
    <svg
      className={className}
      fill="none"
      viewBox="0 0 24 24"
    >
      <path
        className="stroke-gray-600 group-hover:stroke-black transition-colors"
        strokeLinecap="round"
        strokeLinejoin="round"
        strokeWidth={4}
        d="M6 18L18 6M6 6l12 12"
      />
    </svg>
  );
}
