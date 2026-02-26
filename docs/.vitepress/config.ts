import { defineConfig } from 'vitepress'

export default defineConfig({
  title: 'AnimeSub',
  description: 'Anime subscription and auto-download application',
  base: '/AnimeSub/',

  locales: {
    zh: {
      label: '中文',
      lang: 'zh-CN',
      title: 'AnimeSub',
      description: '动漫订阅与自动下载应用',
      themeConfig: {
        nav: [
          { text: '项目介绍', link: '/zh/introduction' },
          { text: '快速开始', link: '/zh/quick-start' },
          { text: '详细文档', link: '/zh/configuration' },
        ],
        sidebar: {
          '/zh/': [
            {
              text: '指南',
              items: [
                { text: '项目介绍', link: '/zh/introduction' },
                { text: '快速开始', link: '/zh/quick-start' },
              ]
            },
            {
              text: '参考',
              items: [
                { text: '配置参考', link: '/zh/configuration' },
                { text: '功能说明', link: '/zh/features' },
              ]
            },
            {
              text: '开发参考',
              items: [
                { text: '架构 & 文件说明 & API', link: '/zh/dev-reference' },
              ]
            },
            {
              text: '其他',
              items: [
                { text: '常见问题', link: '/zh/faq' },
                { text: '更新日志', link: '/zh/changelog' },
              ]
            }
          ]
        }
      }
    },
    en: {
      label: 'English',
      lang: 'en-US',
      title: 'AnimeSub',
      description: 'Anime subscription and auto-download application',
      themeConfig: {
        nav: [
          { text: 'Introduction', link: '/en/introduction' },
          { text: 'Quick Start', link: '/en/quick-start' },
          { text: 'Docs', link: '/en/configuration' },
        ],
        sidebar: {
          '/en/': [
            {
              text: 'Guide',
              items: [
                { text: 'Introduction', link: '/en/introduction' },
                { text: 'Quick Start', link: '/en/quick-start' },
              ]
            },
            {
              text: 'Reference',
              items: [
                { text: 'Configuration', link: '/en/configuration' },
                { text: 'Features', link: '/en/features' },
              ]
            },
            {
              text: 'Developer Reference',
              items: [
                { text: 'Architecture & Files & API', link: '/en/dev-reference' },
              ]
            },
            {
              text: 'More',
              items: [
                { text: 'FAQ', link: '/en/faq' },
                { text: 'Changelog', link: '/en/changelog' },
              ]
            }
          ]
        }
      }
    }
  },

  themeConfig: {
    socialLinks: [
      { icon: 'github', link: 'https://github.com/hhlyyng/AnimeSub' }
    ],
    search: {
      provider: 'local'
    },
    footer: {
      message: 'Released under the MIT License.',
      copyright: 'Copyright © 2026 AnimeSub Contributors'
    }
  }
})
