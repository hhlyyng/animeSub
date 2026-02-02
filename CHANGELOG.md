# Changelog

项目改动记录，遵循 React Best Practices 进行代码重构。

---

## 2026-02-02

### Sidebar 按钮与标题垂直对齐修复

#### 改动点

1. **为 `.sidebar-header` 添加 `items-center`**

#### 原因

**为什么要这样改：**

- 上一次重构移除了 `items-center`（因为它与 `h-[44px]` 冲突）
- 但移除后导致 toggle button 图标和 "Anime-Sub" 文字的水平中轴线不对齐

**这样改的好处：**

- 现在没有 `h-[44px]` 的干扰，`items-center` 可以正常工作
- 按钮图标和文字在垂直方向上居中对齐，视觉一致

#### 问题与解决方案

| 问题 | 解决方案 |
|------|----------|
| toggle button 和 "Anime-Sub" 垂直不对齐 | 添加 `items-center` 到 `.sidebar-header` |

#### 示例代码

**index.css - 之前：**

```css
.sidebar-header {
  @apply w-full px-4 pt-16 pb-4 flex flex-row gap-3 mb-6;
}
```

**index.css - 之后：**

```css
.sidebar-header {
  @apply w-full px-4 pt-16 pb-4 flex flex-row items-center gap-3 mb-6;
}
```

#### 相关文件

- `frontend/src/index.css`

---

### 前端间距样式重构

#### 改动点

1. **合并 `index.css` 中重复的 `.sidebar-header` 定义**
2. **移除 `SideBar.tsx` 中的冗余内联样式 `!pt-15`**
3. **新增统一的 `.content-header` CSS 类**
4. **`HomePageContent.tsx` 使用 CSS 类替代内联 padding**

#### 原因

**为什么要这样改：**

- 原代码中 `.sidebar-header` 在 `index.css` 中定义了两次（第 126-128 行和第 130-132 行），后者覆盖前者，造成维护困难
- `h-[44px]` + `items-center` 与 `!pt-15` 同时存在时产生样式冲突，导致标题实际位置难以预测
- 间距值 `pt-15` 分散在组件内联样式中，违反 DRY 原则
- SideBar 的 "Anime-Sub" 和主内容的 "今日放送" 标题无法对齐

**这样改的好处：**

- 单一数据源：间距值统一定义在 CSS 类中，修改一处即可全局生效
- 可预测性：移除 `h-[44px]` 和 `items-center` 的干扰，padding 直接控制位置
- 可维护性：CSS 类语义化命名 `.content-header`，代码意图清晰
- 一致性：两个标题都使用 `pt-16` (64px)，确保视觉对齐

#### 问题与解决方案

| 问题 | 解决方案 |
|------|----------|
| `.sidebar-header` 重复定义 | 合并为单一定义 |
| `h-[44px]` + `items-center` 与 `pt-15` 冲突 | 移除 height 和 items-center，使用纯 padding 控制 |
| 间距值分散在内联样式 `!pt-15` | 统一定义 `.content-header` CSS 类 |
| "Anime-Sub" 与 "今日放送" 标题不对齐 | 两者都使用 `pt-16` (64px) |

#### 示例代码

**index.css - 之前：**

```css
/* 重复定义，造成混乱 */
.sidebar-header {
  @apply w-full px-2 py-3 flex items-center justify-start;
}

.sidebar-header {
  @apply w-full h-[44px] px-4 flex items-center gap-2 mb-10;
}
```

**index.css - 之后：**

```css
/* 顶部标题区域 - 统一使用 pt-16 (64px) 作为页面顶部间距 */
.sidebar-header {
  @apply w-full px-4 pt-16 pb-4 flex flex-row gap-3 mb-6;
}

/* 主内容区顶部间距 - 与 sidebar-header 对齐 */
.content-header {
  @apply pt-16;
}
```

**SideBar.tsx - 之前：**

```tsx
<div className="sidebar-header flex flex-row gap-3 !justify-start !pt-15">
```

**SideBar.tsx - 之后：**

```tsx
<div className="sidebar-header">
```

**HomePageContent.tsx - 之前：**

```tsx
<div className="w-fit overflow-hidden pt-15">
```

**HomePageContent.tsx - 之后：**

```tsx
<div className="w-fit overflow-hidden content-header">
```

#### 相关文件

- `frontend/src/index.css`
- `frontend/src/components/homepage/SideBar.tsx`
- `frontend/src/components/homepage/content/HomePageContent.tsx`

#### 参考

- [Vercel React Best Practices](https://github.com/vercel-labs/agent-skills)
- 规则：避免重复定义、单一数据源、语义化 CSS 类名
