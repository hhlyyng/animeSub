type Variant = "toggle" | "action" | "user";

type SidebarButtonProps = {
  icon: React.ReactNode;
  label: string;          // 用于 Tooltip 的文字
  variant: Variant;
  ariaLabel?: string;
  onClick?: () => void;
  active?: boolean;
  collapsed?: boolean;
  onToggleHover?: (hovered: boolean) => void; 
  showtext: boolean; 
  language?: "en" | "zh";  // 父组件传入的折叠状态
};

export function SidebarButton({
  icon, label, variant, ariaLabel, onClick, active, collapsed, onToggleHover, showtext, language="zh"
}: SidebarButtonProps) {

  const handleMouseEnter = (e: React.MouseEvent<HTMLButtonElement>) => {
    if (variant === "toggle") {
      // toggle 按钮的特殊处理
      onToggleHover?.(true);
      (e.target as HTMLButtonElement).style.backgroundColor = "transparent";
    } else if (!active && variant === "action") {
      // action 按钮的悬停效果
      (e.target as HTMLButtonElement).style.backgroundColor = "rgba(209,225,229,0.3)";
    }
  };

  const handleMouseLeave = (e: React.MouseEvent<HTMLButtonElement>) => {
    if (variant === "toggle") {
      // toggle 按钮的特殊处理
      onToggleHover?.(false);
      (e.target as HTMLButtonElement).style.backgroundColor = "transparent";
    } else if (!active && variant === "action") {
      // action 按钮的悬停效果
      (e.target as HTMLButtonElement).style.backgroundColor = "transparent";
    }
  };

  return (
    <button
      type="button"
      onClick={onClick}
      className={`
        sidebar-button justify-start relative group
        ${collapsed ? 'sidebar-button--collapsed' : 'sidebar-button--expanded'}
      `}
      onMouseEnter={handleMouseEnter}
      onMouseLeave={handleMouseLeave}
      data-variant={variant}
      aria-current={active ? "page" : undefined}
      aria-label={ariaLabel || label}
    >
      <span className="sidebar-button-icon">{icon}</span>
      {!collapsed && showtext && (
        <span className="sidebar-button-label whitespace-nowrap">{label}</span>
      )}
  
      {/* Tooltip，仅在折叠时显示；作为 button 的子元素定位 */}
      {collapsed && (
        <span
          className="
            pointer-events-none absolute left-full top-1/2 -translate-y-1/2 ml-2
            px-2 py-1 text-sm text-white bg-gray-800 rounded shadow-lg
            opacity-0 group-hover:opacity-100 whitespace-nowrap
            transition-opacity duration-200
          "
        >
          {variant !== "toggle" ? label : language === "en"? "Expand":"展开"}
        </span>
      )}
    </button>
  );
}