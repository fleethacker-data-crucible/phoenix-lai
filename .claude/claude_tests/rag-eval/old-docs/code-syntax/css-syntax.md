# CSS syntax reference (Phoenix)

For Phoenix models writing and checking CSS (current CSS Level 3+ modules, as supported by Chrome, Edge, Firefox and Safari). Each `###` entry is one rule, selector group, property group or function: what it does, its exact syntax, every value, an example, and the mistakes to check for.

**Notation:** `<length>` = number + unit (`0` may omit the unit) · `[x]` optional · `a | b` one of · `a || b` one or both in any order · `{n,m}` repeated n to m times · `#` comma-separated list.

## Checklist: before you answer with CSS

- Every declaration is `property: value;`, with a colon, not `=`. Separate declarations with `;`, and give every rule matching `{ }`.
- Lengths other than 0 need a unit: `width: 10px`, not `width: 10`. There is no space between the number and the unit (`10 px` is invalid). Exceptions with no unit: `line-height`, `z-index`, `opacity`, `flex-grow`, `order`, `font-weight`.
- Property and value names are real. `text-color` and `font-color` do not exist (use `color`), and neither does `background-colour`. Check the value keywords too.
- Selectors: `.class`, `#id`, `element`, `[attr]`. A space means descendant. `.a.b` (no space) means both classes on one element.
- Comments are `/* */` only. `//` is not a comment in CSS: it breaks the following rule.
- `!important` goes after the value, inside the semicolon: `color: red !important;`.
- Custom properties are case-sensitive, start with `--` and are used through `var(--name, fallback)`.
- Inside `calc()`, `+` and `-` need spaces on both sides: `calc(100% - 20px)`.
- `z-index` works only on positioned elements (position other than `static`), flex/grid items, and elements that make a stacking context.
- Percent `height` needs a parent with a definite height. Consider `min-height: 100vh` or flex/grid instead.
- Vendor prefixes (`-webkit-`) are rarely needed today. Keep the unprefixed property last.

## Syntax and cascade

### Rules, declarations and comments (CSS syntax)

**Syntax**

```css
selector [, selector ...] {
  property: value [!important];
  property: value1 value2, value3;     /* space-separated parts; comma-separated lists */
}
/* comment */
```

**Parts**

- Selector list: rules apply to every comma-separated selector. One invalid selector in a list drops the whole rule, except inside `:is()` and `:where()`.
- An invalid declaration is ignored, and the rest of the rule still applies.

**Check**

- A missing `}` swallows every rule after it.

### Cascade, specificity and inheritance (CSS concepts)

The winning declaration is chosen in this order: importance (`!important`) and origin, then cascade layer (`@layer`, where later wins and unlayered beats layered), then specificity, then source order (later wins).

**Specificity (a, b, c):** a = ids · b = classes, attributes, pseudo-classes · c = elements, pseudo-elements. `:where()` adds 0. `:is()`, `:not()` and `:has()` add the specificity of their most specific argument. Inline `style=""` beats every selector.

**Inherited properties:** `color`, `font-*`, `line-height`, `letter-spacing`, `text-align`, `text-indent`, `white-space`, `visibility`, `cursor`, `list-style`, `direction`, `quotes`. Most others are not inherited (margins, borders, background, width).

**Global values** (allowed on every property): `inherit` · `initial` · `unset` (inherit if inherited, else initial) · `revert` · `revert-layer`.

**Check**

- When a rule "does not apply", look for a more specific selector or `!important` elsewhere. Do not escalate with `!important`.

### @-rules: @import, @media, @supports, @layer, @container, @font-face, @keyframes, @page, @property, @scope, @starting-style, @charset, @namespace (CSS at-rules)

**Syntax**

```css
@charset "utf-8";                                   /* first line only */
@import url("base.css") [layer(base)] [supports(display: grid)] [screen and (min-width: 600px)];   /* before all other rules */
@layer reset, base, components;                     /* declare order */
@layer components { .btn { padding: 4px 8px; } }
@media screen and (min-width: 600px) and (prefers-color-scheme: dark) { ... }
@media (400px <= width <= 800px) { ... }            /* range syntax */
@supports (display: grid) and (not (display: inline-grid)) { ... }
@supports selector(:has(a)) { ... }
@container sidebar (min-width: 300px) { ... }       /* needs container-type on an ancestor */
@font-face { font-family: "Inter"; src: url(inter.woff2) format("woff2"); font-weight: 100 900; font-style: normal; font-display: swap; unicode-range: U+0000-00FF; }
@keyframes spin { from { transform: rotate(0deg); } to { transform: rotate(360deg); } }
@page { size: A4 portrait; margin: 2cm; }   @page :first { margin-top: 4cm; }
@property --angle { syntax: "<angle>"; inherits: false; initial-value: 0deg; }
@scope (.card) to (.content) { img { border: 1px solid; } }
@starting-style { .toast { opacity: 0; } }          /* entry animation start values */
@namespace svg url(http://www.w3.org/2000/svg);
```

**Media features:** `width`, `height`, `min-width`, `max-width`, `aspect-ratio`, `orientation` (portrait, landscape), `resolution` (`2dppx`), `hover` (hover, none), `pointer` (fine, coarse, none), `any-hover`, `any-pointer`, `prefers-color-scheme` (light, dark), `prefers-reduced-motion` (reduce, no-preference), `prefers-contrast` (more, less, custom), `forced-colors` (active), `display-mode` (standalone, fullscreen), `print`, `screen`.

**Check**

- `@import` must come before every other rule except `@charset` and `@layer` statements, or it is ignored.
- `@media` conditions are joined with `and`. Commas mean "or".

## Selectors

### Basic and attribute selectors (CSS selectors)

**Syntax**

```css
*                   /* every element */
p                   /* type */
.note               /* class */
#main               /* id */
a.btn.primary       /* compound: all must match */
[href]              /* has attribute */
[type="text"]       /* exact value */
[class~="warn"]     /* word in space-separated list */
[lang|="en"]        /* "en" or starts with "en-" */
[href^="https"]     /* starts with */
[href$=".pdf"]      /* ends with */
[href*="example"]   /* contains */
[data-x="a" i]      /* case-insensitive value; s = case-sensitive */
```

### Combinators and nesting (CSS selectors)

**Syntax**

```css
nav a          /* descendant (any depth) */
ul > li        /* child */
h2 + p         /* next sibling (immediately after) */
h2 ~ p         /* any later sibling */
.card {                       /* native nesting (2023+ browsers) */
  color: black;
  & .title { font-weight: bold; }       /* .card .title */
  &:hover { color: blue; }              /* .card:hover */
  .dark & { color: white; }             /* .dark .card */
  @media (width > 600px) { padding: 2rem; }
}
```

**Check**

- In native CSS nesting, `&:hover` needs the `&`. A bare `:hover` inside a nested rule means a descendant `*:hover`.

### Pseudo-classes (CSS selectors)

**Syntax**

```css
:hover :active :focus :focus-visible :focus-within :visited :link :any-link :target
:first-child :last-child :only-child :nth-child(2n+1 | odd | even | 3 | -n+3 [of .sel]) :nth-last-child(n)
:first-of-type :last-of-type :only-of-type :nth-of-type(n) :nth-last-of-type(n) :empty :root
:not(.a, .b) :is(h1, h2) :where(ul, ol) :has(> img) :has(+ .error)
:checked :disabled :enabled :required :optional :valid :invalid :user-valid :user-invalid :in-range :out-of-range
:placeholder-shown :read-only :read-write :default :indeterminate :autofill
:popover-open :modal :open :fullscreen :defined :lang(fr) :dir(rtl) :scope :host :host(.x) :host-context(.x) :state(x)
```

**Check**

- Use `:focus-visible` for keyboard focus rings, so mouse clicks do not show them.
- `:nth-child(2)` counts every sibling, not only those that match the type. Use `:nth-of-type` to count one element type.

### Pseudo-elements (CSS selectors)

**Syntax**

```css
::before ::after        /* need content: "" to render */
::first-line ::first-letter ::marker ::placeholder ::selection ::backdrop ::file-selector-button
::part(name) ::slotted(span) ::cue ::highlight(name) ::view-transition-old(x) ::view-transition-new(x)
.req::after { content: " *"; color: red; }
```

**Check**

- `::before` and `::after` render nothing without a `content` property. They do not work on void elements such as `<img>` and `<input>`.

## Values and units

### Units (CSS values)

**Syntax**

```css
px  em (parent font size; for font-size: of parent)  rem (root font size)  %  ex  ch (width of "0")  cap  ic  lh  rlh
vw  vh  vmin  vmax  svh/lvh/dvh (small/large/dynamic viewport)  svw  dvw   cqw cqh cqi cqb cqmin cqmax (container query units)
cm mm Q in pt pc   deg rad grad turn   s ms   Hz kHz   dpi dpcm dppx x   fr (grid fraction)
```

**Check**

- `100vh` on mobile includes the area behind the browser bars. Use `100dvh` or `100svh`.
- `em` compounds in nested elements. Use `rem` for consistent sizes.

### Colors (CSS values)

**Syntax**

```css
color: red;  color: #f00;  color: #ff000080;  color: transparent;  color: currentColor;
color: rgb(255 0 0);  color: rgb(255 0 0 / 50%);  color: rgba(255, 0, 0, 0.5);       /* legacy comma form */
color: hsl(210 50% 40%);  color: hsl(210deg 50% 40% / .8);
color: hwb(210 10% 20%);  color: lab(50% 40 -20);  color: lch(60% 50 270);  color: oklab(0.6 0.1 -0.1);  color: oklch(70% 0.15 250);
color: color(display-p3 1 0 0);  color: color-mix(in oklch, red 40%, blue);  color: light-dark(#111, #eee);
color: rgb(from var(--brand) r g b / 50%);     /* relative color */
```

**Check**

- Do not mix commas and spaces in one call: `rgb(255, 0, 0 / 50%)` is invalid.
- `light-dark()` works only when `color-scheme: light dark` is set.

### Functions: calc, min, max, clamp, var, env, attr, url (CSS values)

**Syntax**

```css
width: calc(100% - 2 * var(--gap));
font-size: clamp(1rem, 2.5vw, 2rem);      /* clamp(MIN, PREFERRED, MAX) */
width: min(100%, 60ch);   height: max(50vh, 300px);
color: var(--text, black);                /* var(--name, fallback) */
padding-bottom: env(safe-area-inset-bottom, 0px);
content: attr(data-label);
background: url("img/bg.png");            /* relative to the CSS file, not the page */
round(nearest, 13px, 5px)  mod(7, 3)  rem(7, 3)  abs(-1px)  sign(x)  sin(45deg)  cos()  tan()  pow(2, 3)  sqrt(9)  hypot(3px, 4px)  log()  exp()
counter(item)  counters(item, ".")
```

**Check**

- `calc(100%-20px)` is invalid because it has no spaces around `-`.
- Custom property names are case-sensitive: `--Main` and `--main` are different.
- `url()` paths resolve against the stylesheet's own location.

### Custom properties (CSS variables)

**Syntax**

```css
:root { --brand: #0a84ff; --gap: 16px; --radius: 8px; }
.card { padding: var(--gap); border-radius: var(--radius); }
.dark { --brand: #4da3ff; }               /* override in a scope; inherited by descendants */
```

**JavaScript:** `el.style.setProperty('--gap', '24px')`, `getComputedStyle(el).getPropertyValue('--gap')`.

**Check**

- A custom property holds tokens, not computed values. `--x: 10;` used as `width: var(--x)px` is invalid. Write `calc(var(--x) * 1px)`.

## Box model and layout

### Box model: width, height, margin, padding, border, box-sizing (CSS properties)

**Syntax**

```css
box-sizing: content-box | border-box;          /* border-box: width includes padding + border */
width | height: auto | <length> | <percentage> | min-content | max-content | fit-content | fit-content(<length>) | stretch;
min-width | max-width | min-height | max-height: <length> | <percentage> | none (max only) | auto (min only);
inline-size | block-size | min-inline-size | max-block-size ...      /* logical (writing-mode aware) */
aspect-ratio: auto | 16 / 9;
margin: <top> [<right> [<bottom> [<left>]]];    /* 1 = all, 2 = vertical horizontal, 3 = top horizontal bottom */
margin: 0 auto;                                 /* center a block with a width */
margin-top | margin-right | margin-bottom | margin-left | margin-block | margin-inline | margin-block-start ...: <length> | <percentage> | auto;
padding: (same shorthand as margin; no auto, no negative values)
border: <width> || <style> || <color>;          /* border: 1px solid #ccc */
border-style: none | hidden | solid | dashed | dotted | double | groove | ridge | inset | outset;
border-width: thin | medium | thick | <length>;   border-color: <color>;
border-top | border-right | border-bottom | border-left | border-block | border-inline: <width> <style> <color>;
border-radius: 8px | 50% | 10px 20px | 10px / 20px;   border-top-left-radius ...
outline: 2px solid blue;  outline-offset: 2px;   /* takes no layout space */
```

**Check**

- Add `*, *::before, *::after { box-sizing: border-box; }` so that `width` includes padding and border.
- Vertical margins of adjacent blocks collapse to the larger one. Flex and grid items do not collapse.
- `margin: auto` centers horizontally only when the element has a width (or in flex and grid).
- A border is invisible without a `border-style`.

### display and visibility (CSS properties)

**Syntax**

```css
display: block | inline | inline-block | flex | inline-flex | grid | inline-grid | flow-root | none | contents
       | table | table-row | table-cell | list-item | ruby | block flex (two-value syntax);
visibility: visible | hidden | collapse;
opacity: 0..1 | 0%..100%;
content-visibility: visible | auto | hidden;
```

**Check**

- `display: none` removes the element from layout. `visibility: hidden` keeps its space. `opacity: 0` keeps both the space and the clicks.
- `width`, `height` and vertical margins do not apply to `inline` elements. Use `inline-block`.

### position, inset and z-index (CSS properties)

**Syntax**

```css
position: static | relative | absolute | fixed | sticky;
top | right | bottom | left: auto | <length> | <percentage>;
inset: 0;                         /* shorthand for top right bottom left */
z-index: auto | <integer>;
```

**Check**

- `absolute` positions against the nearest positioned ancestor (one with a position other than `static`). Give the parent `position: relative`.
- `sticky` needs a `top` (or another inset), and does not work when an ancestor has `overflow: hidden` or `auto`.
- `fixed` breaks when an ancestor has `transform`, `filter` or `perspective`, because that ancestor becomes the containing block.

### Flexbox (CSS layout)

**Syntax**

```css
.container {
  display: flex | inline-flex;
  flex-direction: row | row-reverse | column | column-reverse;
  flex-wrap: nowrap | wrap | wrap-reverse;
  flex-flow: <direction> || <wrap>;
  justify-content: flex-start | flex-end | center | space-between | space-around | space-evenly | start | end | left | right | stretch;   /* main axis */
  align-items: stretch | flex-start | flex-end | center | baseline | first baseline | last baseline | start | end;   /* cross axis */
  align-content: (same values as justify-content, plus stretch);   /* only with wrapping lines */
  gap: <row-gap> [<column-gap>];   row-gap: 8px;   column-gap: 8px;
}
.item {
  flex: none | auto | <grow> [<shrink>] [<basis>];   /* flex: 1 = 1 1 0%; flex: auto = 1 1 auto; none = 0 0 auto */
  flex-grow: <number>;  flex-shrink: <number>;  flex-basis: auto | content | <length> | <percentage>;
  align-self: auto | stretch | center | flex-start | flex-end | baseline;
  order: <integer>;
}
```

**Example**

```css
.toolbar { display: flex; align-items: center; gap: 8px; }
.toolbar .spacer { flex: 1; }               /* pushes later items right */
.center { display: flex; justify-content: center; align-items: center; min-height: 100vh; }
```

**Check**

- `justify-content` works on the main axis, so with `flex-direction: column` it is vertical.
- Items do not shrink below their content size, because `min-width: auto` applies. Add `min-width: 0` (or `overflow: hidden`) to let text truncate inside a flex child.

### Grid (CSS layout)

**Syntax**

```css
.grid {
  display: grid | inline-grid;
  grid-template-columns: 200px 1fr 2fr | repeat(3, 1fr) | repeat(auto-fill | auto-fit, minmax(200px, 1fr)) | [name] 1fr [end] | subgrid | masonry;
  grid-template-rows: auto 1fr auto;
  grid-template-areas: "header header" "side main" "footer footer";
  grid-template: <rows> / <columns>;
  grid-auto-rows: minmax(100px, auto);   grid-auto-columns: 1fr;   grid-auto-flow: row | column | dense | row dense;
  gap: 16px 8px;
  justify-items | align-items: start | end | center | stretch;   place-items: center;
  justify-content | align-content: start | end | center | stretch | space-between | space-around | space-evenly;
}
.item {
  grid-column: 1 / 3 | 2 / span 2 | 1 / -1 (full width);   grid-row: 1 / 2;
  grid-column-start | grid-column-end | grid-row-start | grid-row-end: <line> | span <n> | <name>;
  grid-area: header | <row-start> / <col-start> / <row-end> / <col-end>;
  justify-self | align-self: start | end | center | stretch;   place-self: center;
}
```

**Check**

- Each row string in `grid-template-areas` must have the same number of cells, and each area must form a rectangle. Use `.` for an empty cell.
- `auto-fit` collapses empty tracks, so the items stretch. `auto-fill` keeps the empty tracks.
- Lines are numbered from 1, and `-1` is the last line.

### Overflow, float, clear, columns (CSS properties)

**Syntax**

```css
overflow: visible | hidden | clip | scroll | auto;   overflow-x / overflow-y;   overflow-wrap: normal | break-word | anywhere;
scrollbar-gutter: stable [both-edges];   scrollbar-width: auto | thin | none;   scrollbar-color: <thumb> <track>;
scroll-behavior: smooth;   scroll-snap-type: x mandatory;   scroll-snap-align: start | center;   scroll-margin-top: 60px;   overscroll-behavior: contain;
float: left | right | none | inline-start | inline-end;   clear: left | right | both | none;
columns: 3 200px;   column-count: 3;   column-width: 200px;   column-gap: 2em;   column-rule: 1px solid #ccc;   column-span: all;   break-inside: avoid;
```

**Check**

- Contain floats with `display: flow-root` on the parent, not an empty `<div style="clear:both">`.

## Typography

### Fonts (CSS properties)

**Syntax**

```css
font: [italic] [small-caps] [bold | 700] <size>[/<line-height>] <family>;    /* size and family are required */
font-family: "Segoe UI", Roboto, system-ui, -apple-system, sans-serif;        /* always end with a generic family */
font-size: <length> | <percentage> | small | medium | large | x-large | smaller | larger | math;
font-weight: normal | bold | lighter | bolder | 100..900;
font-style: normal | italic | oblique [<angle>];
font-variant: normal | small-caps | ...;   font-variant-numeric: tabular-nums | lining-nums | slashed-zero;
font-stretch: 50%..200% | condensed | expanded;   font-display (in @font-face): auto | block | swap | fallback | optional;
font-feature-settings: "liga" 0, "tnum";   font-variation-settings: "wght" 650;   font-optical-sizing: auto;   font-size-adjust: 0.5;
line-height: normal | <number> | <length> | <percentage>;      /* unitless number (1.5) is best */
```

**Generic families:** `serif`, `sans-serif`, `monospace`, `cursive`, `fantasy`, `system-ui`, `ui-sans-serif`, `ui-serif`, `ui-monospace`, `ui-rounded`, `math`, `emoji`.

**Check**

- Quote family names that contain spaces. Never quote the generic keywords: `"sans-serif"` is looked up as a font name.
- In the `font` shorthand, the order is fixed: style, variant and weight come before the size, and the family is last.

### Text (CSS properties)

**Syntax**

```css
color: <color>;
text-align: left | right | center | justify | start | end | match-parent;   text-align-last: auto | center ...;
text-decoration: <line> || <style> || <color> || <thickness>;   /* underline dotted red 2px */
text-decoration-line: none | underline | overline | line-through;   text-underline-offset: 3px;   text-decoration-skip-ink: auto;
text-transform: none | uppercase | lowercase | capitalize | full-width;
text-indent: <length>;   letter-spacing: normal | <length>;   word-spacing: normal | <length>;
white-space: normal | nowrap | pre | pre-wrap | pre-line | break-spaces;   white-space-collapse; text-wrap: wrap | nowrap | balance | pretty | stable;
word-break: normal | break-all | keep-all;   hyphens: none | manual | auto;   line-break: auto | strict;
text-overflow: clip | ellipsis;               /* needs overflow: hidden + white-space: nowrap (single line) */
-webkit-line-clamp: 3;  display: -webkit-box;  -webkit-box-orient: vertical;  overflow: hidden;   /* multi-line clamp */
text-shadow: 1px 1px 2px rgba(0,0,0,.5);
vertical-align: baseline | top | middle | bottom | text-top | text-bottom | sub | super | <length>;   /* inline and table-cell only */
writing-mode: horizontal-tb | vertical-rl | vertical-lr;   direction: ltr | rtl;   unicode-bidi;   tab-size: 4;
list-style: <type> || <position> || <image>;   list-style-type: disc | circle | square | decimal | lower-alpha | upper-roman | none | "→ ";
```

**Check**

- `text-overflow: ellipsis` alone does nothing. Add `overflow: hidden; white-space: nowrap;` and a constrained width.
- `vertical-align` does not center blocks. Use flex or grid.

## Backgrounds, effects and visuals

### Backgrounds and gradients (CSS properties)

**Syntax**

```css
background: <color> | <image> <position> / <size> <repeat> <attachment> <origin> <clip>;   /* many layers comma-separated; color last */
background-color: <color>;
background-image: url(a.png) | linear-gradient(...) | none;
background-position: center | top right | 50% 50% | right 10px bottom 10px;
background-size: auto | cover | contain | 100px 50px;
background-repeat: repeat | no-repeat | repeat-x | repeat-y | space | round;
background-attachment: scroll | fixed | local;   background-origin | background-clip: border-box | padding-box | content-box | text;
linear-gradient([<angle> | to <side>], <color-stop>#)        /* linear-gradient(to right, #fff, #000 80%) */
radial-gradient([circle | ellipse] [<size>] [at <position>], <color-stop>#)
conic-gradient([from <angle>] [at <position>], <color-stop>#)
repeating-linear-gradient(...)   image-set("a.png" 1x, "a@2x.png" 2x)
```

**Check**

- In `background` with a `/`, the size must directly follow the position: `center / cover`.
- `background-clip: text` also needs `color: transparent` (and `-webkit-background-clip: text` in some browsers).

### Shadows, filters, blend, masks, clipping (CSS properties)

**Syntax**

```css
box-shadow: [inset] <x> <y> [<blur> [<spread>]] <color> [, ...];       /* 0 2px 8px rgba(0,0,0,.2) */
filter: blur(4px) brightness(1.2) contrast(1.1) grayscale(1) hue-rotate(90deg) invert(1) opacity(.5) saturate(2) sepia(1) drop-shadow(0 2px 4px #0005);
backdrop-filter: blur(10px);
mix-blend-mode: normal | multiply | screen | overlay | darken | lighten | difference ...;   isolation: isolate;
clip-path: inset(10px round 8px) | circle(50%) | ellipse() | polygon(50% 0, 100% 100%, 0 100%) | path("...");
mask: url(m.svg) center / contain no-repeat;   mask-image: linear-gradient(black, transparent);
object-fit: fill | contain | cover | none | scale-down;   object-position: center;     /* img, video */
cursor: auto | default | pointer | text | move | grab | grabbing | not-allowed | wait | progress | crosshair | help | zoom-in | col-resize | url(c.png) 4 4, auto;
pointer-events: auto | none;   user-select: auto | none | text | all;   touch-action: auto | none | pan-x | pan-y | manipulation;
appearance: auto | none;   accent-color: <color>;   caret-color: <color>;   color-scheme: light dark;   resize: none | both | horizontal | vertical;
will-change: transform, opacity;   contain: layout paint;   container-type: inline-size | size | normal;   container-name: sidebar;
```

## Transforms, transitions and animation

### transform (CSS property)

**Syntax**

```css
transform: none | translate(10px, 20px) | translateX() | translateY() | translateZ() | translate3d(x, y, z)
         | scale(1.2) | scaleX() | scaleY() | rotate(45deg) | rotateX() | rotateY() | rotateZ() | rotate3d(x, y, z, a)
         | skew(10deg, 5deg) | skewX() | skewY() | matrix(a, b, c, d, e, f) | perspective(500px);    /* several applied right to left */
translate: 10px 20px;   rotate: 45deg;   scale: 1.2;      /* individual properties */
transform-origin: center | top left | 50% 50% 0;   transform-style: flat | preserve-3d;   perspective: 800px;   backface-visibility: hidden;
```

**Check**

- Separate several functions with spaces, not commas: `transform: translateX(10px) rotate(5deg);`.
- `transform` has no effect on non-replaced `inline` elements.

### transition (CSS property)

**Syntax**

```css
transition: <property> <duration> [<timing-function>] [<delay>] [allow-discrete] [, ...];
transition: opacity 200ms ease-out, transform 300ms cubic-bezier(.2, .8, .2, 1) 50ms;
transition-property: all | none | <property>#;   transition-duration: 0.3s;   transition-delay: 0s;
transition-timing-function: ease | linear | ease-in | ease-out | ease-in-out | step-start | step-end | steps(4, end) | cubic-bezier(x1, y1, x2, y2) | linear(0, .5 25%, 1);
transition-behavior: normal | allow-discrete;     /* lets display / overlay transition */
```

**Check**

- `display: none` and `height: auto` do not transition by default. Use `opacity`, `transform`, `grid-template-rows: 0fr` → `1fr`, or `allow-discrete` with `@starting-style`.
- A duration needs a unit: write `0.3s`, not `0.3`.

### animation and @keyframes (CSS property)

**Syntax**

```css
@keyframes pulse { 0% { opacity: 1; } 50% { opacity: .4; } 100% { opacity: 1; } }
animation: <name> <duration> [<timing>] [<delay>] [<iteration-count>] [<direction>] [<fill-mode>] [<play-state>];
animation: pulse 1.5s ease-in-out 0s infinite alternate both;
animation-name | animation-duration | animation-timing-function | animation-delay;
animation-iteration-count: <number> | infinite;   animation-direction: normal | reverse | alternate | alternate-reverse;
animation-fill-mode: none | forwards | backwards | both;   animation-play-state: running | paused;   animation-composition: replace | add;
animation-timeline: scroll() | view() | --name;   animation-range: entry 0% cover 50%;
@media (prefers-reduced-motion: reduce) { * { animation: none !important; transition: none !important; } }
```

**Check**

- The `@keyframes` name must match `animation-name` exactly, with no quotes in the shorthand.
- Without `forwards`, the element jumps back to its original style after the animation ends.

## Responsive design

### Responsive patterns (CSS techniques)

**Syntax**

```css
img, video { max-width: 100%; height: auto; }
.container { width: min(100% - 2rem, 1100px); margin-inline: auto; }
.cards { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 1rem; }
@media (max-width: 600px) { .sidebar { display: none; } }
.wrapper { container-type: inline-size; }  @container (min-width: 500px) { .card { display: flex; } }
h1 { font-size: clamp(1.5rem, 4vw, 3rem); }
:root { color-scheme: light dark; }  @media (prefers-color-scheme: dark) { :root { --bg: #111; } }
```

**Check**

- Without `<meta name="viewport" content="width=device-width, initial-scale=1">` in the HTML, phones render at a 980px width and media queries never fire.
