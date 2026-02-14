type BellIconProps = {
  className?: string;
  active?: boolean;
};

export function BellIcon({ className = "w-5 h-5", active = false }: BellIconProps) {
  return (
    <svg
      className={className}
      viewBox="0 0 24 24"
      fill={active ? "currentColor" : "none"}
      aria-hidden="true"
    >
      <path
        d="M15 17H9M18 17H6C7.33333 15.6667 8 13.6667 8 11V9C8 6.79086 9.79086 5 12 5C14.2091 5 16 6.79086 16 9V11C16 13.6667 16.6667 15.6667 18 17Z"
        stroke="currentColor"
        strokeWidth="1.8"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <path
        d="M10.5 19C10.8866 19.5826 11.4096 20 12 20C12.5904 20 13.1134 19.5826 13.5 19"
        stroke="currentColor"
        strokeWidth="1.8"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );
}
