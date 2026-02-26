<template>
  <div class="hp">

    <!-- ─── HERO ───────────────────────────────────── -->
    <section class="hp-hero">
      <h1 class="hp-title">{{ t.title }}</h1>
      <p class="hp-tagline">{{ t.tagline }}</p>
      <div class="hp-screenshot-wrap">
        <img
          :src="withBase(t.screenshot)"
          :alt="t.title + ' screenshot'"
          class="hp-screenshot"
        />
      </div>
      <div class="hp-actions">
        <a class="hp-btn-primary" :href="withBase(t.startLink)">{{ t.startBtn }}</a>
        <a class="hp-btn-ghost" href="https://github.com/hhlyyng/AnimeSub" target="_blank">
          GitHub <svg width="12" height="12" viewBox="0 0 12 12" fill="currentColor"><path d="M3.5 3a.5.5 0 0 0 0 1H7.3L2.15 9.15a.5.5 0 1 0 .7.7L8 4.7V8.5a.5.5 0 0 0 1 0v-5a.5.5 0 0 0-.5-.5h-5Z"/></svg>
        </a>
      </div>
    </section>

    <!-- ─── RULE ───────────────────────────────────── -->
    <div class="hp-rule" />

    <!-- ─── FEATURES ──────────────────────────────── -->
    <section class="hp-features">
      <div class="hp-feat-grid">
        <div
          v-for="(f, i) in t.features"
          :key="i"
          class="hp-feat"
        >
          <span class="hp-feat-num">{{ String(i + 1).padStart(2, '0') }}</span>
          <h3 class="hp-feat-title">{{ f.title }}</h3>
          <p class="hp-feat-desc">{{ f.desc }}</p>
        </div>
      </div>
    </section>

    <!-- ─── RULE ───────────────────────────────────── -->
    <div class="hp-rule" />

    <!-- ─── DEPLOY ─────────────────────────────────── -->
    <section class="hp-deploy">
      <div class="hp-deploy-inner">

        <div class="hp-deploy-copy">
          <h2 class="hp-deploy-title">{{ t.deployTitle }}</h2>
          <p class="hp-deploy-desc">{{ t.deployDesc }}</p>
          <a class="hp-btn-primary" :href="withBase(t.startLink)">{{ t.deployBtn }}</a>
        </div>

        <div class="hp-terminal">
          <div class="hp-terminal-bar">
            <span class="hp-dot hp-dot-r" />
            <span class="hp-dot hp-dot-y" />
            <span class="hp-dot hp-dot-g" />
            <span class="hp-terminal-title">bash</span>
          </div>
          <pre class="hp-terminal-body"><span class="hp-cmt">{{ t.code.c1 }}</span>
mkdir animesub &amp;&amp; cd animesub

<span class="hp-cmt">{{ t.code.c2 }}</span>
docker compose up -d

<span class="hp-cmt">{{ t.code.c3 }}</span>
open http://localhost:3000</pre>
        </div>

      </div>
    </section>

  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { withBase } from 'vitepress'

const props = defineProps<{ lang: 'zh' | 'en' }>()

const i18n = {
  zh: {
    title: 'AnimeSub',
    tagline: '自动化追番下载工具',
    screenshot: '/homepage_zh.png',
    startBtn: '快速开始',
    startLink: '/zh/quick-start',
    deployTitle: '30 秒部署到本地',
    deployDesc: '基于 Docker，无需配置运行环境。首次启动后跟随安装向导，5 步完成全部配置。',
    deployBtn: '查看部署文档',
    code: { c1: '# 创建目录', c2: '# 启动服务', c3: '# 打开浏览器' },
    features: [
      {
        title: '每日放送',
        desc: '按星期展示本周动漫放送时间表，数据来自 Bangumi，附带 TMDB 横版背景图与中英双语简介。',
      },
      {
        title: '订阅追番',
        desc: '通过 Mikan RSS 订阅番剧，支持指定字幕组、关键词包含与排除过滤，精准捕获每一集更新。',
      },
      {
        title: '自动下载',
        desc: '后台 RSS 轮询服务每 30 分钟自动检查，新资源直接推送到 qBittorrent，全程无需人工介入。',
      },
      {
        title: 'Top 10 排行',
        desc: '同时展示 Bangumi 评分榜、AniList 趋势榜、MyAnimeList 热门榜，三源并列一目了然。',
      },
      {
        title: 'Web 设置向导',
        desc: '5 步浏览器内安装向导，账户创建、qBittorrent 配置、TMDB Token 填写一站完成。',
      },
      {
        title: 'JWT 鉴权',
        desc: '完整认证体系：bcrypt 密码加密、JWT Token 鉴权，所有 API 受保护，数据安全可控。',
      },
    ],
  },
  en: {
    title: 'AnimeSub',
    tagline: 'Automated anime download manager',
    screenshot: '/homepage_en.png',
    startBtn: 'Get Started',
    startLink: '/en/quick-start',
    deployTitle: 'Deploy in 30 seconds',
    deployDesc: 'Docker-based — no environment setup required. Follow the browser setup wizard to configure everything in 5 steps.',
    deployBtn: 'View Deploy Guide',
    code: { c1: '# Create directory', c2: '# Start service', c3: '# Open browser' },
    features: [
      {
        title: 'Daily Schedule',
        desc: 'Weekly airing timetable sourced from Bangumi, with TMDB landscape backdrops and bilingual descriptions.',
      },
      {
        title: 'Subscriptions',
        desc: 'Subscribe to series via Mikan RSS. Filter by fansub group and keywords to capture exactly the episodes you want.',
      },
      {
        title: 'Auto Download',
        desc: 'Background RSS polling checks every 30 minutes and pushes new torrents to qBittorrent — no manual action needed.',
      },
      {
        title: 'Top 10 Charts',
        desc: 'Three simultaneous rankings from Bangumi, AniList Trending, and MyAnimeList — all at a glance.',
      },
      {
        title: 'Setup Wizard',
        desc: 'A 5-step browser-based wizard to configure accounts, qBittorrent, TMDB token, and preferences.',
      },
      {
        title: 'JWT Auth',
        desc: 'Full authentication with bcrypt password hashing and JWT tokens. All API endpoints protected.',
      },
    ],
  },
}

const t = computed(() => i18n[props.lang])
</script>

<style scoped>
/* ── Root ───────────────────────────────────────── */
.hp {
  max-width: 1000px;
  margin: 0 auto;
  padding: 0 32px;
}

/* ── Hero ───────────────────────────────────────── */
.hp-hero {
  padding: 100px 0 88px;
  text-align: center;
}

.hp-eyebrow {
  font-size: 12px;
  font-weight: 600;
  letter-spacing: 0.12em;
  text-transform: uppercase;
  color: var(--vp-c-brand-1);
  margin: 0 0 28px;
}

.hp-title {
  font-size: clamp(40px, 5.5vw, 64px);
  font-weight: 800;
  line-height: 1.1;
  letter-spacing: -0.025em;
  color: var(--vp-c-text-1);
  margin: 0 0 20px;
}

.hp-tagline {
  font-size: 18px;
  line-height: 1.75;
  color: var(--vp-c-text-2);
  max-width: 600px;
  margin: 0 auto 44px;
}

.hp-screenshot-wrap {
  margin: 36px auto 44px;
  max-width: 880px;
  border-radius: 12px;
  overflow: hidden;
  border: 1px solid var(--vp-c-divider);
  box-shadow: 0 8px 40px rgba(0, 0, 0, 0.12);
}

.hp-screenshot {
  display: block;
  width: 100%;
  height: auto;
}

.hp-actions {
  display: flex;
  gap: 12px;
  justify-content: center;
  flex-wrap: wrap;
}

/* ── Buttons ────────────────────────────────────── */
.hp-btn-primary {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 11px 26px;
  background: var(--vp-c-brand-1);
  color: #fff !important;
  border-radius: 7px;
  font-size: 15px;
  font-weight: 600;
  text-decoration: none !important;
  transition: opacity 0.15s, transform 0.15s;
}
.hp-btn-primary:hover {
  opacity: 0.88;
  transform: translateY(-1px);
}

.hp-btn-ghost {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  padding: 11px 22px;
  background: transparent;
  color: var(--vp-c-text-1) !important;
  border: 1px solid var(--vp-c-divider);
  border-radius: 7px;
  font-size: 15px;
  font-weight: 500;
  text-decoration: none !important;
  transition: border-color 0.15s, transform 0.15s;
}
.hp-btn-ghost:hover {
  border-color: var(--vp-c-text-2);
  transform: translateY(-1px);
}

/* ── Rule ───────────────────────────────────────── */
.hp-rule {
  height: 1px;
  background: var(--vp-c-divider);
}

/* ── Features ───────────────────────────────────── */
.hp-features {
  padding: 80px 0;
}

.hp-feat-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 56px 48px;
}

.hp-feat {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.hp-feat-num {
  font-size: 12px;
  font-weight: 700;
  font-family: var(--vp-font-family-mono);
  color: var(--vp-c-brand-1);
  letter-spacing: 0.08em;
  line-height: 1;
}

.hp-feat-title {
  font-size: 19px;
  font-weight: 700;
  color: var(--vp-c-text-1);
  margin: 0;
  line-height: 1.3;
}

.hp-feat-desc {
  font-size: 15px;
  line-height: 1.75;
  color: var(--vp-c-text-2);
  margin: 0;
}

/* ── Deploy ─────────────────────────────────────── */
.hp-deploy {
  padding: 80px 0 100px;
}

.hp-deploy-inner {
  display: grid;
  grid-template-columns: 1fr 1.1fr;
  gap: 56px;
  align-items: center;
}

.hp-deploy-title {
  font-size: clamp(28px, 3.5vw, 38px);
  font-weight: 800;
  letter-spacing: -0.025em;
  line-height: 1.15;
  color: var(--vp-c-text-1);
  margin: 12px 0 16px;
}

.hp-deploy-desc {
  font-size: 15px;
  line-height: 1.75;
  color: var(--vp-c-text-2);
  margin: 0 0 28px;
}

/* ── Terminal ───────────────────────────────────── */
.hp-terminal {
  border-radius: 10px;
  overflow: hidden;
  border: 1px solid var(--vp-c-divider);
  background: var(--vp-c-bg-alt);
}

.hp-terminal-bar {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 11px 14px;
  border-bottom: 1px solid var(--vp-c-divider);
}

.hp-terminal-title {
  font-size: 12px;
  font-family: var(--vp-font-family-mono);
  color: var(--vp-c-text-3);
  margin-left: 6px;
}

.hp-dot {
  width: 11px;
  height: 11px;
  border-radius: 50%;
}
.hp-dot-r { background: #ff5f57; }
.hp-dot-y { background: #febc2e; }
.hp-dot-g { background: #28c840; }

.hp-terminal-body {
  padding: 20px 20px 24px;
  margin: 0;
  font-family: var(--vp-font-family-mono);
  font-size: 13px;
  line-height: 1.9;
  color: var(--vp-c-text-1);
  overflow-x: auto;
  white-space: pre;
}

.hp-cmt {
  color: var(--vp-c-text-3);
}

/* ── Responsive ─────────────────────────────────── */
@media (max-width: 800px) {
  .hp { padding: 0 24px; }
  .hp-hero { padding: 72px 0 64px; }
  .hp-feat-grid { grid-template-columns: 1fr 1fr; gap: 40px 32px; }
  .hp-deploy-inner { grid-template-columns: 1fr; }
  .hp-deploy { padding: 64px 0 80px; }
}

@media (max-width: 520px) {
  .hp-feat-grid { grid-template-columns: 1fr; gap: 36px; }
  .hp-tagline { font-size: 16px; }
}
</style>
