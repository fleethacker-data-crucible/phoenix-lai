## Essentials

Target: modern CSS in current browsers (flexbox, grid, custom properties, `clamp()`, `:has()`).

| Never | Write instead |
|---|---|
| `color = red`, missing `;` | `color: red;` |
| `width: 10`, `margin: 5 px` | `width: 10px`, `margin: 5px` (unit, no space; `0` needs none) |
| `text-color`, `font-color`, `background-colour` | `color`, `background-color` |
| `// comment` | `/* comment */` (`//` breaks the next rule) |
| `calc(100%-20px)` | `calc(100% - 20px)` (spaces around + and -) |
| `color: red; !important` | `color: red !important;` |
| floats and tables for layout | `display: flex` / `display: grid` |
| fixed `height: 100%` without a sized parent | `min-height: 100vh` or flex / grid |

Always: every rule has matching `{ }`; property and value names are real; custom properties are `--name` used as `var(--name, fallback)`; `z-index` needs a positioned element (or a flex / grid item). Mobile first: base styles, then `@media (min-width: ...)`.

## Recipes

### Center something horizontally and vertically (flex, grid place-items)

**Example**

```css
.center-flex {
  display: flex;
  justify-content: center;
  align-items: center;
  min-height: 100vh;
}
.center-grid {
  display: grid;
  place-items: center;
}
```

### A responsive card grid (grid, repeat, auto-fill, minmax, gap)

**Example**

```css
.cards {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(240px, 1fr));
  gap: 1rem;
}
.card {
  padding: 1rem;
  border: 1px solid #ddd;
  border-radius: 8px;
  background: #fff;
}
```

### Theme colors with custom properties and dark mode (var, prefers-color-scheme)

**Example**

```css
:root {
  --bg: #ffffff;
  --text: #1d1d1f;
  --accent: #0a66c2;
}
@media (prefers-color-scheme: dark) {
  :root {
    --bg: #121212;
    --text: #e8e8e8;
  }
}
body {
  background: var(--bg);
  color: var(--text);
}
a {
  color: var(--accent);
}
```

### A sticky header and a scrolling area (position: sticky, overflow)

**Example**

```css
.header {
  position: sticky;
  top: 0;
  z-index: 10;
  background: var(--bg, #fff);
  border-bottom: 1px solid #ddd;
}
.scroll {
  max-height: 400px;
  overflow-y: auto;
}
```

### Responsive typography and layout breakpoints (clamp, @media)

**Example**

```css
h1 {
  font-size: clamp(1.5rem, 1rem + 2vw, 3rem);
  line-height: 1.2;
}
.layout {
  display: grid;
  grid-template-columns: 1fr;
  gap: 1rem;
}
@media (min-width: 768px) {
  .layout {
    grid-template-columns: 240px 1fr;
  }
}
```

### Buttons with hover, focus and transitions (:hover, :focus-visible, transition)

**Example**

```css
.btn {
  display: inline-block;
  padding: 0.5rem 1rem;
  border: 0;
  border-radius: 6px;
  background: #0a66c2;
  color: #fff;
  cursor: pointer;
  transition: background-color 0.15s ease;
}
.btn:hover {
  background-color: #084e96;
}
.btn:focus-visible {
  outline: 3px solid #f5b400;
  outline-offset: 2px;
}
```
