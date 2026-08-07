# FortOS Official Website

The official website for [FortOS](https://github.com/GeneralLibrary/fortOS) — a modern, security-first Linux NAS operating system built on .NET 10.

Built with [Astro](https://astro.build) + Tailwind CSS, based on the [Astroship](https://astroship.web3templates.com) starter template. Fully bilingual (English / 中文) with built-in i18n routing.

## 🚀 Development

```bash
npm install        # or: corepack pnpm install
npm run dev        # start dev server at http://localhost:4321
```

## 🏗️ Build

```bash
npm run build      # static output in dist/
npm run preview    # preview the production build
```

## 🌐 i18n

- Default locale: `en` (served at `/`)
- Chinese: served at `/zh/`
- Locale routing is configured in `astro.config.mjs`
- All copy lives in `src/i18n.ts` (en / zh dictionaries)

## ✏️ Content

- Homepage sections: `src/components/` (hero, features, logos, cta, navbar, footer)
- Pages: `src/pages/` (index, about, deploy, blog, contact, 404)
- Blog posts: `src/content/blog/` (Markdown, per-language files with a `lang` frontmatter field)
- Site config: `astro.config.mjs` (site URL, i18n, integrations)

## 🔧 Configuration

Before deploying, update the `site` URL in `astro.config.mjs` and the `Sitemap` URL in `public/robots.txt` to your real domain.

## 🙏 Credits

Built with the [Astroship](https://astroship.web3templates.com) starter template by [Web3Templates](https://web3templates.com).
