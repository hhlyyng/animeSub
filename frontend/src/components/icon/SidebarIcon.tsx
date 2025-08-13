const DefaultSideBar = () => (
    <svg viewBox="0 0 24 24" fill="none"
         stroke="currentColor" strokeWidth={2}
         strokeLinecap="round" strokeLinejoin="round">
      <rect x="4" y="7" width="16" height="10" rx="2" />
      <line x1="8" y1="7" x2="8" y2="17" />
    </svg>
  );
  
  const ExpandSideBarArrow = () => (
    <svg viewBox="0 0 24 24" fill="none"
         stroke="currentColor" strokeWidth={2}
         strokeLinecap="round" strokeLinejoin="round">
      {/* 箭身（水平） */}
      <line x1="6" y1="12" x2="16" y2="12" />
      {/* 箭头（指向右） */}
      <polyline points="12 8 16 12 12 16" />
      {/* 终止竖线（右边） */}
      <line x1="18" y1="6" x2="18" y2="18" />
    </svg>
  );
  
  const CollapseSideBarArrow = () => (
    <svg viewBox="0 0 24 24" fill="none"
         stroke="currentColor" strokeWidth={2}
         strokeLinecap="round" strokeLinejoin="round">
      {/* 终止竖线（左边） */}
      <line x1="6" y1="6" x2="6" y2="18" />
      {/* 箭头（指向左） */}
      <polyline points="12 8 8 12 12 16" />
      {/* 箭身（水平） */}
      <line x1="8" y1="12" x2="18" y2="12" />
    </svg>
  );
  
  export { DefaultSideBar, CollapseSideBarArrow, ExpandSideBarArrow };