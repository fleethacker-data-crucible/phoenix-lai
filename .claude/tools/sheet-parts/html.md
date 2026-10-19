## Essentials

Target: HTML5 (the living standard) in current browsers.

| Never | Write instead |
|---|---|
| `</br>`, `<input></input>`, `<img ...></img>` | void elements have no end tag: `<br>`, `<input>`, `<img>` |
| `<p><div>...</div></p>`, `<p><ul>...</ul></p>` | a `<p>` holds only inline content: close it before blocks |
| `<li>` outside a list, `<td>` outside `<tr>` | `<ul><li>..</li></ul>`, `<table><tbody><tr><td>..</td></tr></tbody></table>` |
| `<center>`, `<font>`, `bgcolor=`, `align=` | CSS (`text-align`, `color`, `display: flex`) |
| `<img src="x.png">` without alt | `<img src="x.png" alt="what it shows">` (`alt=""` for decoration) |
| `<input>` without a label | `<label for="email">Email</label> <input id="email" name="email" type="email">` |
| `onclick="..."`, `href="javascript:..."` | `addEventListener` in a script |
| two elements with the same `id` | unique ids; classes for groups |

Always: `<!DOCTYPE html>`, `<html lang="en">`, `<meta charset="utf-8">` first in `<head>`, a viewport meta, a `<title>`. Every non-void element closed, properly nested. Escape `<` and `&` in text. A `<button>` in a form is `type="button"` unless it submits. Scripts last in `<body>` or `defer`.

## Recipes

### A complete page skeleton (doctype, head, viewport, main)

**Example**

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Orders</title>
  <link rel="stylesheet" href="styles.css">
</head>
<body>
  <header><h1>Orders</h1></header>
  <main id="content"><p>Loading…</p></main>
  <footer><small>&copy; 2024 Example</small></footer>
  <script src="app.js" defer></script>
</body>
</html>
```

### An accessible form with validation (label, required, type, pattern, fieldset)

**Example**

```html
<form action="/signup" method="post">
  <fieldset>
    <legend>Sign up</legend>
    <label for="name">Name</label>
    <input id="name" name="name" type="text" required autocomplete="name">
    <label for="email">Email</label>
    <input id="email" name="email" type="email" required autocomplete="email">
    <label for="plan">Plan</label>
    <select id="plan" name="plan">
      <option value="free">Free</option>
      <option value="pro" selected>Pro</option>
    </select>
    <label><input type="checkbox" name="terms" required> I accept the terms</label>
    <button type="submit">Create account</button>
  </fieldset>
</form>
```

### A data table with header, caption and footer (table, thead, tbody, th scope)

**Example**

```html
<table>
  <caption>Orders this week</caption>
  <thead>
    <tr><th scope="col">Order</th><th scope="col">Customer</th><th scope="col">Amount</th></tr>
  </thead>
  <tbody>
    <tr><td>1001</td><td>Ann</td><td>120.50</td></tr>
    <tr><td>1002</td><td>Bob</td><td>80.00</td></tr>
  </tbody>
  <tfoot>
    <tr><th scope="row" colspan="2">Total</th><td>200.50</td></tr>
  </tfoot>
</table>
```

### Responsive images and media (picture, srcset, video with controls)

**Example**

```html
<picture>
  <source srcset="hero.avif" type="image/avif">
  <img src="hero.jpg" alt="Team at work" width="1200" height="600" loading="lazy">
</picture>
<video controls width="640" preload="metadata">
  <source src="intro.mp4" type="video/mp4">
  Your browser cannot play this video.
</video>
```

### Navigation and page structure (nav, ul, aria-current)

**Example**

```html
<nav aria-label="Main">
  <ul>
    <li><a href="/" aria-current="page">Home</a></li>
    <li><a href="/orders">Orders</a></li>
    <li><a href="/help">Help</a></li>
  </ul>
</nav>
```

### A dialog opened by a button (dialog, showModal)

**Example**

```html
<button type="button" id="open">Details</button>
<dialog id="details">
  <h2>Order 1001</h2>
  <p>Shipped on 2024-05-01.</p>
  <form method="dialog"><button>Close</button></form>
</dialog>
<script>
  document.getElementById('open').addEventListener('click', () => document.getElementById('details').showModal());
</script>
```
