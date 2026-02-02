# Changelog

项目改动记录，遵循 React Best Practices 进行代码重构。

---

## 2026-02-02

### AnimeDetailModal 优化重构

#### 改动概述

1. **Modal 模糊效果优化** - 只模糊内容区，Sidebar 保持清晰
2. **全局语言状态集成** - Modal 根据全局语言设置显示对应语言的标题和描述
3. **布局重构** - 改为上下结构（信息区 + 下载源区）
4. **链接按钮样式优化** - 去除彩色背景，改为透明边框样式
5. **关闭按钮样式优化** - 纯 X 图标，无背景无边框

#### Junior Developer 学习要点

##### 1. Zustand 状态管理 - 跨组件通信

**场景**: Modal 打开时需要通知 HomePage 对内容区应用模糊效果

**方案**: 在 Zustand store 中添加共享状态

```tsx
// useAppStores.tsx - 添加 Modal 状态
interface AppState {
  isModalOpen: boolean;
  setModalOpen: (open: boolean) => void;
}

// AnimeInfoFlow.tsx - 打开 Modal 时设置状态
const setGlobalModalOpen = useAppStore((state) => state.setModalOpen);
const handleAnimeSelect = (anime: AnimeInfo) => {
  setSelectedAnime(anime);
  setGlobalModalOpen(true);  // 通知全局
};

// HomePage.tsx - 消费状态，条件渲染模糊
const isModalOpen = useAppStore((state) => state.isModalOpen);
<main className={`... ${isModalOpen ? 'blur-md pointer-events-none' : ''}`}>
```

**要点**: 使用 selector `(state) => state.xxx` 避免不必要的重渲染

##### 2. React Portal - Modal 渲染到 body

**场景**: Modal 需要脱离组件层级，避免被父元素的 `overflow: hidden` 或 `z-index` 影响

```tsx
import { createPortal } from "react-dom";

// 将 Modal 内容渲染到 document.body
return createPortal(modalContent, document.body);
```

**要点**: Portal 改变 DOM 位置但保留 React 事件冒泡

##### 3. 覆盖全局 CSS - all: unset

**场景**: 全局 CSS 给 `button` 设置了默认样式，需要完全重置

```css
/* index.css 中的全局按钮样式 */
button {
  background-color: #1a1a1a;
  border: 1px solid transparent;
}
button:hover {
  border-color: #646cff;
}
```

```tsx
// 使用 all: unset 完全重置
<button
  style={{
    all: 'unset',  // 重置所有继承和默认样式
    cursor: 'pointer',
    position: 'absolute',
    // ... 其他需要的样式
  }}
>
```

**要点**: `all: unset` 比逐个覆盖属性更彻底，适合需要完全自定义的组件

##### 4. Tailwind group-hover - 父子联动效果

**场景**: hover 按钮时改变内部 SVG 图标颜色

```tsx
<button className="group">
  <svg>
    <path className="stroke-gray-600 group-hover:stroke-black transition-colors" />
  </svg>
</button>
```

**要点**: `group` 标记父元素，`group-hover:` 在子元素上响应父元素的 hover

##### 5. 语言切换的 fallback 模式

**场景**: 根据语言显示标题，但某些数据可能缺失

```tsx
const language = useAppStore((state) => state.language);

// 带 fallback 的语言选择
const primaryTitle = language === 'zh'
  ? (anime.ch_title || anime.en_title)   // 中文优先，fallback 英文
  : (anime.en_title || anime.ch_title);  // 英文优先，fallback 中文

const description = language === 'zh'
  ? (anime.ch_desc || anime.en_desc || '暂无描述')
  : (anime.en_desc || anime.ch_desc || 'No description available');
```

**要点**: 使用 `||` 链式 fallback，确保始终有内容显示

#### 问题与解决方案

| 问题 | 解决方案 |
|------|----------|
| Modal 模糊整个页面包括 Sidebar | 使用 Zustand 共享状态，只对内容区 `<main>` 应用 blur |
| Modal 被父元素样式影响 | 使用 `createPortal` 渲染到 `document.body` |
| 全局 button 样式干扰关闭按钮 | 使用 `all: unset` 完全重置样式 |
| hover 效果在按钮方块上而非图标上 | 移除背景，使用 `group-hover` 只改变图标颜色 |
| 多语言内容可能缺失 | 使用 `||` 链式 fallback |

#### 相关文件

- `frontend/src/stores/useAppStores.tsx` - 新增 `isModalOpen` 状态
- `frontend/src/components/homepage/HomePage.tsx` - 条件模糊
- `frontend/src/components/homepage/content/AnimeDetailModal.tsx` - 重构布局和样式
- `frontend/src/components/homepage/content/AnimeInfoFlow.tsx` - 调用 `setModalOpen`

---

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
