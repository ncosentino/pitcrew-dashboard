---
# AUTO-GENERATED from .github/instructions/genesis/astro/content-collections.instructions.md — do not edit
paths:
  - "**/content.config.ts"
  - "**/content/**/*.md"
  - "**/content/**/*.mdx"
  - "**/content/**/*.json"
---
# Astro Content Collections Rules

## Schema definition

Every content collection is defined in `src/content.config.ts` with a `loader` (where the entries
come from) and a Zod `schema` (the shape each entry must match). Use the built-in `glob()` loader
from `astro/loaders` to load a folder of Markdown, MDX, JSON, YAML, or TOML files:

```typescript
import { defineCollection } from 'astro:content';
import { glob, file } from 'astro/loaders';
import { z } from 'astro/zod';

// glob(): a folder of files, one entry per file. Best for prose with a body (Markdown/MDX),
// e.g. blog posts, where each entry is its own document.
const blog = defineCollection({
  loader: glob({ pattern: '**/*.{md,mdx}', base: './src/content/blog' }),
  schema: z.object({
    title: z.string(),
    description: z.string(),
    date: z.date(),
    draft: z.boolean().default(false),
    tags: z.array(z.string()).default([]),
  }),
});

// file(): a single file holding an array of entries. Best for small structured data with no
// body (e.g. FAQs, team members, pricing) — the whole list stays in one editable file. Each
// array entry needs a unique `id`; the schema validates the remaining fields. Entries are
// returned sorted by `id`, so include an explicit `order` field and sort in the component if
// you need a specific display order (array order is not preserved).
const faqs = defineCollection({
  loader: file('src/content/faqs.json'),
  schema: z.object({
    order: z.number(),
    question: z.string(),
    answer: z.string(),
  }),
});

export const collections = { blog, faqs };
```

Do not use untyped content. Every field accessed in templates must be declared in the schema. The
schema validates each entry at build time, so a missing or mistyped field fails the build with a
clear error instead of shipping a broken page.

## Folder structure

The config lives at `src/content.config.ts` (the project `src/` root — not inside `src/content/`).
Each collection is a folder of entry files under `src/content/`:

```
src/
  content.config.ts
  content/
    blog/
      my-first-post.md
      astro-best-practices.mdx
    faqs.json
```

## Frontmatter

Every markdown/MDX file must include frontmatter matching its collection schema:

```markdown
---
title: "My First Post"
description: "A brief introduction to the blog."
date: 2025-01-15
tags: ["astro", "web"]
---
```

## Querying

Use `getCollection()` in pages, never raw file imports:

```astro
---
import { getCollection } from 'astro:content';

const posts = await getCollection('blog', ({ data }) => !data.draft);
---
```

Filter drafts in production. Sort by date descending for blog-style content.

## MDX

Use MDX (`.mdx`) when content needs interactive components. Use plain markdown (`.md`) for
pure text content. Do not use MDX everywhere — it adds build overhead.

## SEO for content

Every content item should have:
- A unique `title` (used for `<title>` and `<h1>`)
- A unique `description` (used for meta description)
- Structured data where appropriate (Article schema for blog posts)
