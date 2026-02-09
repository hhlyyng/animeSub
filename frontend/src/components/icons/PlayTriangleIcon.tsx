type PlayTriangleIconProps = {
  className?: string;
};

export default function PlayTriangleIcon({ className = "" }: PlayTriangleIconProps) {
  return (
    <svg
      viewBox="0 0 24 24"
      xmlns="http://www.w3.org/2000/svg"
      className={className}
      fill="currentColor"
      aria-hidden="true"
    >
      <path d="M8 5.75a1 1 0 0 1 1.51-.86l9.5 5.75a1 1 0 0 1 0 1.72l-9.5 5.75A1 1 0 0 1 8 17.25V5.75z" />
    </svg>
  );
}
