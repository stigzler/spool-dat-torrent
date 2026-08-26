# Progress Bar Height Issue — Investigation Notes

## The problem
In the StreamCard's collapsible file list, the per-file `MudProgressLinear` bars render at slightly different heights (varying by ~1–2px) even though the intent is a consistent 4px bar.

## What was tried (in order)

1. **Inline `Style="height: 4px"`** on the `MudProgressLinear` component (in `StreamCard.razor`). No effect — the style doesn't reach MudBlazor's inner bar element.

2. **Global CSS targeting `.mud-progress-linear` and `.mud-progress-linear-bar`** (in `app.css`), setting `height: 4px`. No effect — because MudBlazor's own stylesheet loads *after* `app.css` and overrides it.

3. **Added `!important`** to those same rules. Still reported as not working.

## Key finding (the actual root cause)
Inspected MudBlazor 9.8.0's compiled CSS (`MudBlazor.min.css`) and found the *real* height rules:

```css
.mud-progress-linear { position: relative; }
.mud-progress-linear.horizontal.mud-progress-linear-small  { height: 4px; }
.mud-progress-linear.horizontal.mud-progress-linear-medium { height: 8px; }
.mud-progress-linear.horizontal.mud-progress-linear-large  { height: 12px; }
```

The earlier note misidentified the root cause. The rule
`.mud-progress-linear { position: absolute; bottom: -1px; height: 2px; }` is actually the
*autocomplete-specific* rule (`.mud-select.mud-autocomplete--with-progress .mud-progress-linear`),
not the general rule.

The real problem:
1. `MudProgressLinear` defaults to `Size.Medium` = **8px**.
2. The file-row bar used inline `Style="height: 4px"`, which fought the global `!important`
   rules in `app.css` and the inner `.mud-progress-linear-bar` (absolutely positioned with
   `top:0; bottom:0`), producing the ~1–2px variance.
3. The global `.mud-progress-linear { height: 4px !important; }` rule was also shrinking
   *every* progress bar in the app (the main stream bar and the client bar), not just file rows.

## Resolution
Use MudBlazor's built-in `Size="Size.Small"` (maps to exactly 4px) and remove the global CSS hacks.

- `StreamCard.razor` file-row bar:
  ```razor
  <MudProgressLinear Value="@(file.Progress * 100)" Color="Color.Primary" Class="mt-1" Size="Size.Small" />
  ```
- Removed the `.mud-progress-linear` / `.mud-progress-linear-bar` `!important` overrides from `app.css`.

## Current state of the code (after fix)

- `StreamCard.razor` (file-row progress bar):
  ```razor
  <MudProgressLinear Value="@(file.Progress * 100)" Color="Color.Primary" Class="mt-1" Size="Size.Small" />
  ```

- `app.css`: the `.mud-progress-linear` / `.mud-progress-linear-bar` `!important` overrides have been removed.

## What a fresh context should investigate

1. **Confirm the CSS is actually being applied** — check the browser DevTools (F12) on a file-row progress bar to see the computed `height` and which rule wins.
2. **Verify the exact DOM class names** — the bar element may be `.mud-progress-linear-bars` or a child, not `.mud-progress-linear-bar`.
3. **Rule out caching** — hard refresh (Ctrl+F5) or restart, since CSS changes may not be picked up.
4. **Consider the `Size` parameter** — `MudProgressLinear` has a `Size` enum (Small/Medium/Large), but it's not pixel-precise, so it may not give exactly 4px.

## Files involved

- `src/spool-dat-torrent.web/Components/Shared/StreamCard.razor` (the file-row progress bar)
- `src/spool-dat-torrent.web/wwwroot/app.css` (the CSS overrides)

## Most likely next step

Inspect the live DOM in DevTools to find the correct selector and confirm whether the `!important` rule is being applied or overridden.
