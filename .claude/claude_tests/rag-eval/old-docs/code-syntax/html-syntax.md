# HTML syntax reference (Phoenix)

For Phoenix models writing and checking HTML (the HTML Living Standard, also called HTML5). Each `###` entry is one element or feature: what it does, its exact syntax, every attribute, an example, and the mistakes to check for.

**Notation:** `<tag attr="value">` · `[attr]` optional · `{a | b}` choose one · **void** = no closing tag and no content · **boolean** attribute = present means true (`disabled`), and its value is ignored.

## Checklist: before you answer with HTML

- The document starts with `<!DOCTYPE html>`, then `<html lang="..">` containing one `<head>` (with `<meta charset="utf-8">` first, `<meta name="viewport" ...>` and `<title>`) and one `<body>`.
- Every non-void element is closed, and elements nest properly: `<b><i>x</i></b>`, not `<b><i>x</b></i>`.
- Void elements (`area base br col embed hr img input link meta source track wbr`) have no closing tag. Writing `<br/>` is allowed; `</br>` and `<input></input>` are wrong.
- Block content is never put inside `<p>`. A `<p>` ends automatically before `<div>`, `<ul>`, `<table>` or `<h1>`, which leaves stray `</p>` tags.
- `<li>` sits only inside `<ul>`, `<ol>` or `<menu>`. `<tr>` sits inside `<table>`, `<thead>`, `<tbody>` or `<tfoot>`. `<td>` and `<th>` sit only inside `<tr>`. `<option>` sits inside `<select>` or `<datalist>`.
- `id` values are unique on the page. Every `<label for>` matches an input `id`.
- Every `<img>` has an `alt` (use `alt=""` for decorative images). Form controls have labels. Buttons inside a form have `type="button"` unless they should submit.
- Attribute values containing spaces or special characters are quoted. Use double quotes throughout.
- In text, `<` and `&` are escaped as `&lt;` and `&amp;`.
- Inline `on*=` handlers and `javascript:` URLs are discouraged. Use `addEventListener` in a script.
- Scripts that touch the page go at the end of `<body>`, or use `<script defer src>` or `type="module"`.

## Document structure

### Document skeleton: <!DOCTYPE>, <html>, <head>, <body> (HTML elements)

**Syntax**

```html
<!DOCTYPE html>
<html lang="en" [dir="ltr | rtl"]>
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Page title</title>
  <link rel="stylesheet" href="styles.css">
  <script src="app.js" defer></script>
</head>
<body>
  ...visible content...
</body>
</html>
```

**Attributes**

- `<html lang>`: the language of the page (BCP 47: `en`, `en-US`, `de`), used by screen readers, hyphenation and translation.
- `<html dir>`: the text direction, `ltr`, `rtl` or `auto`.

**Check**

- Only one `<head>` and one `<body>`. Metadata elements (`meta`, `link`, `title`, `style`, `base`, `script`) belong in `<head>`. Visible content goes in `<body>`.
- `<meta charset>` must be within the first 1024 bytes.

### <meta> (HTML void element)

Document metadata.

**Syntax**

```html
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="description" content="Short summary for search results">
<meta name="author" content="Name">   <meta name="robots" content="noindex, nofollow">
<meta name="theme-color" content="#1e1e1e" [media="(prefers-color-scheme: dark)"]>
<meta name="color-scheme" content="light dark">
<meta http-equiv="Content-Security-Policy" content="default-src 'self'">
<meta http-equiv="refresh" content="5; url=next.html">
<meta property="og:title" content="Title">   <!-- Open Graph (social previews) -->
```

**Attributes**

- `charset`: the character encoding. Always `utf-8`.
- `name` with `content`: named metadata (viewport, description, keywords, author, generator, robots, theme-color, color-scheme, referrer).
- `http-equiv` with `content`: an HTTP header equivalent (`content-security-policy`, `refresh`, `x-ua-compatible`, `default-style`).
- `media`: a media query (for theme-color only).

### <title>, <base>, <link>, <style> (HTML head elements)

**Syntax**

```html
<title>Text only, no tags</title>
<base href="https://example.com/app/" [target="_blank"]>          <!-- void; at most one; before any URL use -->
<link rel="stylesheet" href="a.css" [media="print"] [crossorigin] [integrity="sha384-..."]>
<link rel="icon" href="favicon.svg" type="image/svg+xml" [sizes="any"]>
<link rel="preload" href="font.woff2" as="font" type="font/woff2" crossorigin>
<link rel="preconnect | dns-prefetch | prefetch | modulepreload | manifest | canonical | alternate | apple-touch-icon" href="...">
<style [media="(max-width: 600px)"]> body { margin: 0; } </style>
```

**Attributes of <link>**

- `rel`: the relationship (required): stylesheet, icon, preload, preconnect, dns-prefetch, prefetch, modulepreload, manifest, canonical, alternate, author, license, next, prev, search, apple-touch-icon.
- `href`: the URL (required).
- `as`: the content type for preload (`script`, `style`, `font`, `image`, `fetch`, `document`).
- `type`: the MIME type.
- `media`: a media query.
- `sizes`: icon sizes (`16x16 32x32` or `any`).
- `crossorigin`: `anonymous` or `use-credentials`.
- `integrity`: the SRI hash.
- `referrerpolicy`.
- `disabled` (stylesheet).
- `fetchpriority`: `high`, `low` or `auto`.
- `hreflang`.

**Check**

- `<link>` is void, so `</link>` is wrong. `<title>` holds text only.
- A preloaded font needs `crossorigin`, even from the same origin.

### <script> and <noscript> (HTML elements)

**Syntax**

```html
<script src="app.js" [defer] [async] [type="module"] [nomodule] [crossorigin] [integrity] [referrerpolicy] [fetchpriority]></script>
<script>
  // inline code; must not contain the text </script>
</script>
<script type="module"> import { f } from './lib.js'; f(); </script>
<script type="application/json" id="data">{"a": 1}</script>   <!-- data block, not run -->
<script type="importmap">{ "imports": { "lib": "./lib/index.js" } }</script>
<noscript>Enable JavaScript to use this page.</noscript>
```

**Attributes**

- `src`: an external script. The element must be empty when `src` is set.
- `defer`: download in parallel, run after the document is parsed, in order. Classic scripts with `src` only.
- `async`: download in parallel and run as soon as the script is ready, in any order.
- `type`: `module` (ES module, deferred by default, strict mode, supports `import`), `importmap`, or a non-JS MIME type for a data block. Omit it for classic JavaScript.
- `nomodule`: runs only in browsers without module support.
- `crossorigin`, `integrity`, `referrerpolicy`, `fetchpriority`: as for `<link>`.

**Check**

- `<script src="x.js" />` is not self-closing in HTML. Always write `</script>`.
- A script in `<head>` without `defer` runs before the body exists, so `document.getElementById` returns null.

## Sections and text content

### Sectioning: <header>, <nav>, <main>, <section>, <article>, <aside>, <footer>, <address>, <hgroup>, <search> (HTML elements)

**Syntax**

```html
<header>site or section header</header>
<nav aria-label="Main"> <ul><li><a href="/">Home</a></li></ul> </nav>
<main>  <!-- one visible main per page: the primary content -->
  <article> <h2>Post title</h2> <p>...</p> <footer>Posted 2024-01-31</footer> </article>
  <section aria-labelledby="s1"> <h2 id="s1">Section</h2> ... </section>
  <aside>related, sidebar</aside>
</main>
<search> <form role="search">...</form> </search>
<footer> <address>Contact: <a href="mailto:a@b.c">a@b.c</a></address> </footer>
```

**Check**

- Use only one `<main>` that is not `hidden`. Do not nest `<main>` inside `<article>`, `<aside>`, `<header>`, `<footer>` or `<nav>`.
- `<section>` should have a heading. If there is no thematic group, use `<div>`.
- `<address>` is contact information for the nearest article or body, not any postal address.

### Headings <h1>-<h6> (HTML elements)

**Syntax**

```html
<h1>Page title</h1>
<h2>Section</h2>
<h3>Subsection</h3>   <!-- h4, h5, h6 -->
```

**Check**

- Do not skip levels (an `<h2>` followed by an `<h4>`), and do not pick a heading level for its size. Style the size with CSS.

### <p>, <div>, <span>, <br>, <hr>, <pre>, <blockquote> (HTML elements)

**Syntax**

```html
<p>Paragraph of phrasing content.</p>
<div class="card">generic block container</div>
<span class="price">generic inline container</span>
Line one<br>Line two          <!-- void: line break inside text -->
<hr>                          <!-- void: thematic break -->
<pre>  preformatted   text, whitespace kept </pre>
<blockquote cite="https://src.example"> <p>Quoted block.</p> </blockquote>
```

**Check**

- Inside `<p>` only phrasing (inline) content is valid. `<p><div>..</div></p>` produces two empty paragraphs.
- Do not use `<br>` to make space between blocks. Use CSS margins.
- In `<pre>`, `<` and `&` must still be escaped.

### Lists: <ul>, <ol>, <li>, <dl>, <dt>, <dd>, <menu> (HTML elements)

**Syntax**

```html
<ul> <li>Item</li> <li>Item <ul><li>Nested</li></ul></li> </ul>
<ol [start="5"] [reversed] [type="1 | a | A | i | I"]> <li [value="10"]>Step</li> </ol>
<dl> <dt>Term</dt> <dd>Definition</dd> </dl>
<menu> <li><button>Copy</button></li> </menu>
```

**Attributes**

- `<ol start>`: the first number.
- `<ol reversed>`: count down.
- `<ol type>`: the marker style.
- `<li value>`: the number of this item (in `<ol>`).

**Check**

- The only valid children of `<ul>` and `<ol>` are `<li>`, plus `<script>` and `<template>`. A nested list goes inside an `<li>`, not directly inside the `<ul>`.

### <figure> and <figcaption> (HTML elements)

**Syntax**

```html
<figure>
  <img src="chart.png" alt="Sales by month, rising from 10 to 40">
  <figcaption>Figure 1: Monthly sales.</figcaption>
</figure>
```

**Check**

- `<figcaption>` must be the first or last child of `<figure>`.

### <details> and <summary> (HTML elements)

Disclosure widget, with no JavaScript needed.

**Syntax**

```html
<details [open] [name="group"]>
  <summary>Click to expand</summary>
  Hidden content
</details>
```

**Attributes**

- `open`: shown expanded (boolean). It toggles when clicked and fires the `toggle` event.
- `name`: details elements with the same name form an accordion, so only one is open at a time.

### <dialog> (HTML element)

**Syntax**

```html
<dialog id="dlg" [open] [closedby="any | closerequest | none"]>
  <form method="dialog"> <p>Sure?</p> <button value="yes">Yes</button> <button value="no">No</button> </form>
</dialog>
<script>
  const d = document.getElementById('dlg');
  d.showModal();             // modal, with ::backdrop and Esc to close; d.show() = non-modal
  d.addEventListener('close', () => console.log(d.returnValue));
</script>
```

**Check**

- Open a modal with `showModal()`, not the `open` attribute, which makes it non-modal.
- A `<form method="dialog">` closes the dialog and sets `returnValue` to the button's `value`.

## Inline text semantics

### <a> anchor / link (HTML element)

**Syntax**

```html
<a href="URL" [target="_self | _blank | _parent | _top | name"] [rel="noopener noreferrer nofollow ..."] [download[="name.ext"]]
   [hreflang] [type] [ping] [referrerpolicy]>link text</a>
<a href="#section-id">Jump</a>  <a href="mailto:a@b.c?subject=Hi">Mail</a>  <a href="tel:+15551234567">Call</a>
```

**Attributes**

- `href`: the target URL, `#id` fragment, `mailto:`, `tel:` or relative path. Without `href`, the element is only a placeholder.
- `target`: where to open it. `_blank` opens a new tab.
- `rel`: the relationship (`noopener`, `noreferrer`, `nofollow`, `external`, `author`, `help`, `license`, `next`, `prev`, `bookmark`, `tag`).
- `download`: save instead of navigating (same-origin URLs only), with an optional file name.

**Check**

- An `<a>` cannot contain another `<a>`, `<button>` or other interactive content.
- Use `<button>` for actions and `<a href>` for navigation. `href="#"` with a click handler is an anti-pattern.

### Phrasing elements: <strong>, <em>, <b>, <i>, <u>, <s>, <small>, <mark>, <code>, <kbd>, <samp>, <var>, <sub>, <sup>, <abbr>, <cite>, <q>, <dfn>, <time>, <data>, <bdi>, <bdo>, <wbr>, <ins>, <del>, <ruby>, <rt>, <rp> (HTML elements)

**Syntax**

```html
<strong>important</strong> <em>stress emphasis</em> <b>attention, no importance</b> <i>alternate voice, term</i>
<u>unarticulated annotation</u> <s>no longer accurate</s> <small>fine print</small> <mark>highlight</mark>
<code>inline code</code> <kbd>Ctrl</kbd>+<kbd>C</kbd> <samp>program output</samp> <var>x</var>
H<sub>2</sub>O  E=mc<sup>2</sup>  <abbr title="HyperText Markup Language">HTML</abbr>
<cite>Book title</cite> <q cite="url">inline quote</q> <dfn>defined term</dfn>
<time datetime="2024-01-31T14:30">Jan 31, 2:30 pm</time> <data value="123">One two three</data>
<bdi>user-name</bdi> <bdo dir="rtl">text</bdo> very<wbr>long<wbr>word
<ins datetime="2024-02-01" cite="url">added</ins> <del datetime="2024-02-01">removed</del>
<ruby>漢<rp>(</rp><rt>kan</rt><rp>)</rp></ruby>
```

**Attributes**

- `<time datetime>`: a machine-readable date or time (`2024-01-31`, `14:30`, `2024-01-31T14:30Z`, `P2D` duration).
- `<data value>`: a machine-readable value.
- `<abbr title>`: the expansion.
- `<q cite>`, `<ins cite>`, `<del cite>`: the source URL.
- `<ins datetime>`, `<del datetime>`: when the change was made.
- `<bdo dir>`: required, `ltr` or `rtl`.

## Images and media

### <img> (HTML void element)

**Syntax**

```html
<img src="photo.jpg" alt="Description" [width="800"] [height="600"] [loading="lazy | eager"] [decoding="async | sync | auto"]
     [srcset="small.jpg 480w, large.jpg 1080w" sizes="(max-width: 600px) 480px, 1080px"] [srcset="a.png 1x, a@2x.png 2x"]
     [crossorigin] [usemap="#map"] [ismap] [referrerpolicy] [fetchpriority="high | low | auto"]>
```

**Attributes**

- `src`: the image URL (required).
- `alt`: a text alternative (required). Use `""` for decorative images.
- `width`, `height`: the intrinsic size in pixels, without units. They reserve space and prevent layout shift.
- `srcset`: candidate files with a width (`480w`) or density (`2x`) descriptor.
- `sizes`: the display width for each media condition. Required with `w` descriptors.
- `loading`: `lazy` defers off-screen images.
- `decoding`: the decode hint.
- `usemap`: links to a `<map name>`.
- `ismap`: server-side map (inside `<a>`).

**Check**

- `width="100px"` is wrong: write numbers only.
- `<img>` is void, so `</img>` is wrong.

### <picture> and <source> (HTML elements)

**Syntax**

```html
<picture>
  <source srcset="hero.avif" type="image/avif">
  <source srcset="hero-wide.webp" media="(min-width: 800px)" type="image/webp">
  <img src="hero.jpg" alt="Hero image">       <!-- required fallback, last -->
</picture>
```

**Attributes of <source>**

- `srcset`: inside `<picture>`.
- `src`: inside `<audio>` or `<video>`.
- `type`: the MIME type. The browser skips formats it cannot play.
- `media`: a media query.
- `sizes`.
- `width`, `height`.

**Check**

- `<img>` must be the last child of `<picture>`. It provides `alt` and the fallback.

### <video> and <audio> (HTML elements)

**Syntax**

```html
<video src="clip.mp4" [controls] [autoplay] [muted] [loop] [playsinline] [poster="thumb.jpg"] [preload="none | metadata | auto"]
       [width="640"] [height="360"] [crossorigin] [disablepictureinpicture] [controlslist="nodownload nofullscreen"]>
  <source src="clip.webm" type="video/webm">
  <source src="clip.mp4" type="video/mp4">
  <track kind="subtitles | captions | descriptions | chapters | metadata" src="en.vtt" srclang="en" label="English" [default]>
  Your browser does not support video.
</video>
<audio controls [autoplay] [loop] [muted] [preload]> <source src="a.mp3" type="audio/mpeg"> </audio>
```

**Check**

- Browsers block `autoplay` with sound. Add `muted` (and `playsinline` for iOS).
- `<track>` files must be WebVTT (`.vtt`).

### <iframe>, <embed>, <object>, <canvas>, <svg>, <map>, <area> (HTML elements)

**Syntax**

```html
<iframe src="page.html" title="Description" [width] [height] [name] [loading="lazy"] [allow="fullscreen; camera"]
        [sandbox="allow-scripts allow-same-origin allow-forms allow-popups ..."] [referrerpolicy] [srcdoc="<p>inline</p>"] [allowfullscreen]></iframe>
<embed src="file.pdf" type="application/pdf" width="600" height="400">     <!-- void -->
<object data="file.pdf" type="application/pdf" width="600" height="400">fallback</object>
<canvas id="c" width="300" height="150">fallback text</canvas>             <!-- drawn by JS: getContext('2d') -->
<svg width="100" height="100" viewBox="0 0 100 100"><circle cx="50" cy="50" r="40" fill="teal"/></svg>
<map name="m"><area shape="rect | circle | poly | default" coords="0,0,50,50" href="a.html" alt="A"></map>
```

**Check**

- An `<iframe>` needs a `title` for accessibility. An empty `sandbox` blocks everything, so add the needed `allow-*` tokens.
- The `<canvas>` `width` and `height` attributes set the drawing buffer. CSS size only scales it, which blurs it.
- `<area>` needs `alt` when it has `href`.

## Tables

### <table> and its parts (HTML elements)

**Syntax**

```html
<table>
  <caption>Quarterly sales</caption>
  <colgroup> <col span="1" class="name"> <col span="2"> </colgroup>
  <thead> <tr> <th scope="col">Region</th> <th scope="col">Q1</th> <th scope="col">Q2</th> </tr> </thead>
  <tbody>
    <tr> <th scope="row">North</th> <td>10</td> <td>12</td> </tr>
    <tr> <th scope="row">South</th> <td colspan="2">n/a</td> </tr>
  </tbody>
  <tfoot> <tr> <th scope="row">Total</th> <td>10</td> <td>12</td> </tr> </tfoot>
</table>
```

**Attributes**

- `<th scope>`: `col`, `row`, `colgroup` or `rowgroup`.
- `<td>`/`<th>` `colspan`, `rowspan`: the number of cells to span.
- `<td>`/`<th>` `headers`: ids of the header cells.
- `<th abbr>`: a short header.
- `<col span>`, `<colgroup span>`: the number of columns.

**Check**

- Each row must have the same total of cells, counting spans.
- `<caption>` must be the first child. Do not use tables for page layout.
- Old presentational attributes (`border`, `cellpadding`, `align`, `bgcolor`, `width` on td) are obsolete. Use CSS.

## Forms

### <form> (HTML element)

**Syntax**

```html
<form action="/submit" method="get | post | dialog" [enctype="application/x-www-form-urlencoded | multipart/form-data | text/plain"]
      [name] [target] [novalidate] [autocomplete="on | off"] [accept-charset="utf-8"] [rel]>
  ...controls...
  <button type="submit">Send</button>
</form>
```

**Attributes**

- `action`: the URL to submit to (default: the current URL).
- `method`: `get` puts the data in the query string, `post` puts it in the body, and `dialog` closes the dialog.
- `enctype`: must be `multipart/form-data` for file uploads.
- `novalidate`: skip the built-in validation.
- `autocomplete`: the default autofill behavior.

**Check**

- Only controls with a `name` are submitted.
- Forms cannot be nested.
- File uploads need `method="post"` and `enctype="multipart/form-data"`.

### <input> (HTML void element)

**Syntax**

```html
<input type="TYPE" name="field" id="field" [value] [placeholder] [required] [disabled] [readonly] [autofocus]
       [autocomplete="name | email | username | current-password | new-password | tel | street-address | postal-code | one-time-code | off ..."]
       [min] [max] [step] [minlength] [maxlength] [pattern="regex"] [size] [list="datalist-id"] [multiple]
       [accept=".png,image/*"] [capture="user | environment"] [checked] [inputmode="text | numeric | decimal | email | tel | url | search | none"]
       [form="form-id"] [formaction] [formmethod] [formenctype] [formnovalidate] [formtarget] [dirname] [spellcheck] [enterkeyhint]>
```

**TYPE values**

`text` (default) · `password` · `email` · `url` · `tel` · `search` · `number` · `range` · `date` · `time` · `datetime-local` · `month` · `week` · `color` · `checkbox` · `radio` · `file` · `hidden` · `submit` · `reset` · `button` · `image` (with `src`, `alt`).

**Attributes**

- `name`: the key in the submitted data.
- `value`: the initial or current value.
- `required`: the field must be filled.
- `min`, `max`, `step`: for number, range and date/time types. `step="any"` allows any decimal.
- `minlength`, `maxlength`: text length.
- `pattern`: a regex the whole value must match (no slashes, anchored automatically).
- `checked`: initial state for checkbox and radio.
- `multiple`: for email and file.
- `accept`: file types.
- `list`: links to a `<datalist>`.
- `readonly`: not editable but submitted. `disabled`: not editable and not submitted.

**Check**

- Radio buttons in one group share the same `name` and have different `value`s.
- An unchecked checkbox is not submitted at all.
- `<input>` is void and its value goes in `value=""`, not in content.
- `type="number"` also gives `e`, `+` and `-` a meaning. Use `inputmode="numeric" pattern="[0-9]*"` for digit-only codes.

### <label> (HTML element)

**Syntax**

```html
<label for="email">Email</label> <input id="email" name="email" type="email">
<label><input type="checkbox" name="agree"> I agree</label>     <!-- wrapping form, no for needed -->
```

**Check**

- `for` must equal an `id`, not a `name`. Each control has exactly one visible label (or `aria-label`).

### <button> (HTML element)

**Syntax**

```html
<button type="submit | reset | button" [name] [value] [disabled] [autofocus] [form] [formaction] [formmethod]
        [formenctype] [formnovalidate] [formtarget] [popovertarget="id"] [popovertargetaction="toggle | show | hide"]
        [command="show-modal | close | request-close | toggle-popover | --custom"] [commandfor="id"]>Label</button>
```

**Check**

- The default `type` inside a form is `submit`, so a "Cancel" or JS button without `type="button"` submits the form.
- A `<button>` must not contain interactive elements (`a`, `button`, `input`).

### <select>, <option>, <optgroup>, <datalist> (HTML elements)

**Syntax**

```html
<select name="color" id="color" [multiple] [size="4"] [required] [disabled] [autocomplete] [form]>
  <option value="">-- choose --</option>
  <optgroup label="Warm">
    <option value="red" [selected]>Red</option>
    <option value="orange" [disabled]>Orange</option>
  </optgroup>
</select>
<input list="browsers" name="browser">
<datalist id="browsers"> <option value="Chrome"> <option value="Firefox"> </datalist>
```

**Check**

- When an option has no `value`, its text is submitted.
- With `required`, the first option must have `value=""` to act as the placeholder.

### <textarea> (HTML element)

**Syntax**

```html
<textarea name="msg" id="msg" [rows="4"] [cols="40"] [placeholder] [required] [readonly] [disabled] [maxlength] [minlength]
          [wrap="soft | hard"] [autocomplete] [spellcheck="true | false"] [dirname]>initial text</textarea>
```

**Check**

- The initial value goes between the tags, not in `value=""`. Whitespace inside the tags is kept, so do not indent the closing tag on a new line by accident.

### <fieldset>, <legend>, <output>, <progress>, <meter> (HTML elements)

**Syntax**

```html
<fieldset [disabled] [name] [form]> <legend>Shipping</legend> ...controls... </fieldset>
<output name="total" for="a b">0</output>
<progress value="70" max="100">70%</progress>            <!-- no value = indeterminate -->
<meter value="0.6" min="0" max="1" low="0.3" high="0.8" optimum="0.9">60%</meter>
```

**Check**

- `<legend>` must be the first child of `<fieldset>`. A disabled fieldset disables every control inside it.

## Global attributes, templates and accessibility

### Global attributes (HTML attributes)

Allowed on every element.

**Syntax**

```html
id="unique"  class="a b"  style="color: red"  title="tooltip"  lang="fr"  dir="ltr | rtl | auto"  hidden[="until-found"]
tabindex="0 | -1 | n"  accesskey="s"  contenteditable="true | false | plaintext-only"  draggable="true | false"
spellcheck="true | false"  translate="yes | no"  inert  autofocus  autocapitalize="off | sentences | words | characters"
enterkeyhint="enter | done | go | next | previous | search | send"  inputmode="..."  nonce="..."
popover[="auto | manual | hint"]  part="name"  slot="name"  is="custom-element"  itemscope itemtype itemprop itemid itemref
data-*="custom value"   (read with element.dataset.camelCaseName)
role="button | dialog | navigation | ..."   aria-*="..."
on<event>="handler()"   (onclick, oninput, onchange, onsubmit, onload, onkeydown ...)
```

**Attributes**

- `tabindex`: `0` puts the element in the tab order, `-1` makes it focusable only from script. Avoid positive values.
- `hidden`: not rendered. `until-found` lets find-in-page reveal it.
- `inert`: the subtree cannot be clicked or focused and is hidden from assistive technology.
- `popover`: makes the element a popover, toggled by `popovertarget` or `showPopover()`.
- `data-*`: custom data. The name is lowercase with hyphens: `data-user-id` is read as `dataset.userId`.

**Check**

- An `id` cannot contain spaces and must be unique.
- `class` takes space-separated names, not commas.

### ARIA and accessibility (HTML attributes)

**Syntax**

```html
<button aria-expanded="false" aria-controls="menu1">Menu</button>
<ul id="menu1" role="menu" hidden>...</ul>
<div role="alert" aria-live="assertive">Saved</div>
<span aria-hidden="true">★</span><span class="visually-hidden">Favorite</span>
<input aria-label="Search" aria-describedby="hint"> <p id="hint">Type 3+ letters</p>
<nav aria-label="Breadcrumb">  <a aria-current="page" href="#">Here</a>
```

**Common attributes:** `aria-label`, `aria-labelledby`, `aria-describedby`, `aria-hidden`, `aria-expanded`, `aria-controls`, `aria-current` (page, step, location, date, true), `aria-live` (off, polite, assertive), `aria-pressed`, `aria-selected`, `aria-checked`, `aria-disabled`, `aria-invalid`, `aria-required`, `aria-haspopup`, `aria-busy`, `aria-valuenow`/`min`/`max`, `aria-modal`.

**Check**

- Prefer native elements (`<button>`, `<a href>`, `<input type="checkbox">`) over `role` on a `<div>`. A `<div role="button">` also needs `tabindex="0"` and Enter/Space key handling.
- Never put `aria-hidden="true"` on focusable elements.

### <template> and <slot> (HTML elements)

**Syntax**

```html
<template id="row-tpl"> <tr><td class="name"></td></tr> </template>
<script>
  const tpl = document.getElementById('row-tpl');
  const clone = tpl.content.cloneNode(true);       // DocumentFragment
  clone.querySelector('.name').textContent = 'Ann';
  document.querySelector('tbody').append(clone);
</script>
<!-- inside a shadow DOM template: <slot name="title">default</slot> -->
```

**Check**

- The content of a template is inert (scripts do not run, images do not load) until it is cloned into the document.

### Character references (HTML entities) (HTML syntax)

**Syntax**

```html
&amp; (&)  &lt; (<)  &gt; (>)  &quot; (")  &apos; (')  &nbsp; (non-breaking space)  &copy; (©)  &reg; (®)  &trade; (™)
&mdash; (—)  &ndash; (–)  &hellip; (…)  &laquo; («)  &raquo; (»)  &euro; (€)  &pound; (£)  &times; (×)  &divide; (÷)
&#169; (decimal)   &#xA9; (hex)
```

**Check**

- Escape `&` in URLs inside attributes as `&amp;` (`href="?a=1&amp;b=2"`). Browsers tolerate a bare `&`, but validators flag it.

### Comments and obsolete elements (HTML syntax)

**Syntax**

```html
<!-- comment: cannot contain -- inside; not nested -->
```

**Obsolete, do not use:** `<center>`, `<font>`, `<big>`, `<tt>`, `<strike>`, `<marquee>`, `<blink>`, `<frame>`, `<frameset>`, `<noframes>`, `<acronym>` (use `<abbr>`), `<applet>`, `<basefont>`, `<dir>`. Also the attributes `align`, `bgcolor`, `border` (except on `<table>`), `cellpadding`, `cellspacing`, `valign` and `width`/`height` on table cells. Use CSS instead.
