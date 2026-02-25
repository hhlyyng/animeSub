import DefaultTheme from 'vitepress/theme'
import AnimeSubHome from './AnimeSubHome.vue'

export default {
  extends: DefaultTheme,
  enhanceApp({ app }: { app: any }) {
    app.component('AnimeSubHome', AnimeSubHome)
  }
}
