type Variant = "toggle" | "action" | "user";

type SidebarButtonProps = {
  icon: React.ReactNode;
  label: string;
  variant: Variant;
  ariaLabel?: string;
  onClick?: () => void;
  active?: boolean;
  collapsed?: boolean;
  onToggleHover?: (hovered: boolean) => void; 
  showtext: boolean; 
  language?: "en" | "zh";
};

export function SidebarButton({
  icon, label, variant, ariaLabel, onClick, active = false, collapsed, onToggleHover, showtext, language = "zh"
}: SidebarButtonProps) {

  const handleMouseEnter = (e: React.MouseEvent<HTMLButtonElement>) => {
    if (variant === "toggle") {
      onToggleHover?.(true);
      (e.target as HTMLButtonElement).style.backgroundColor = "transparent";
    } else if (!active && variant === "action") {
      (e.target as HTMLButtonElement).style.backgroundColor = "rgba(209,225,229,0.3)";
    }
  };

  const handleMouseLeave = (e: React.MouseEvent<HTMLButtonElement>) => {
    if (variant === "toggle") {
      onToggleHover?.(false);
      (e.target as HTMLButtonElement).style.backgroundColor = "transparent";
    } else if (!active && variant === "action") {
      // 如果是激活状态，保持激活样式；否则清除悬停样式
      (e.target as HTMLButtonElement).style.backgroundColor = active ? "rgba(59, 130, 246, 0.1)" : "transparent";
    }
  };

  return (
    <button
      type="button"
      onClick={onClick}
      className={`
        sidebar-button justify-start relative group
        ${collapsed ? 'sidebar-button--collapsed' : 'sidebar-button--expanded'}
        ${active ? 'text-blue-600' : ''}
      `}
      onMouseEnter={handleMouseEnter}
      onMouseLeave={handleMouseLeave}
      data-variant={variant}
      aria-current={active ? "page" : undefined}
      aria-label={ariaLabel || label}
      style={{
        backgroundColor: active ? "rgba(59, 130, 246, 0.1)" : "transparent"
      }}
    >
      <span className="sidebar-button-icon">{icon}</span>
      {!collapsed && showtext && (
        <span className="sidebar-button-label whitespace-nowrap">{label}</span>
      )}
  
      {/* Tooltip，仅在折叠时显示 */}
      {collapsed && (
        <span
          className={`
            pointer-events-none absolute top-1/2 -translate-y-1/2
            px-2 py-1 text-sm text-white bg-gray-800 rounded shadow-lg
            opacity-0 group-hover:opacity-100 whitespace-nowrap
            transition-opacity duration-200 z-50
            ${variant === "toggle" 
              ? "left-[50px]"  // toggle 按钮的偏移
              : "left-[58px]"  // action 按钮的偏移
            }
          `}
        >
          {variant !== "toggle" ? label : language === "en" ? "Expand" : "展开"}
        </span>
      )}
    </button>
  );
}