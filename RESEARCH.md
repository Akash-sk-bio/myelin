# BioPulse Cursors — Research & Design Brief

## 1. Brand read: instagram.com/akashsk.ai

| Signal | What the profile shows | How it becomes cursor design |
|---|---|---|
| Identity | "Akash Bio AI", digital creator: Business, Technology, Growth | Theme name **BioPulse**: bio-tech HUD, precise and confident |
| Backgrounds | Near-black navy, deep teal-black | Dark "glass" cursor core (`#0B1222`) that stays readable on light backgrounds too |
| Neon accents | Cyan glow on device rings and SpO2 digits, teal mono labels | Primary neon edge (`#22D3EE`) drawn as a glowing tube |
| Highlights | Amber/gold keywords (GERM, PAPER, HORSE, GOLD), gold dust particles | Accent colour (`#FFB020`): LED "pulse" dot, badges, click sparks |
| Alerts | Red glucose readout, red "YOUR SUGAR TEST" bar | Alert colour for the *Unavailable* cursor and the Deadline mood |
| Bio motifs | DNA, E. coli capsule, oximeter, glucometer | DNA-helix busy spinner, helix trail effect, heartbeat LED |
| Profile photo | Electric royal-blue backdrop, black suit | "Business · Royal" mood preset |
| Type | Condensed bold caps plus small mono "BIOLOGY, DECODED" pills | Studio UI: Bahnschrift with Cascadia Mono / Consolas labels |

## 2. Style research: what modern cursor themes do (2025–26)

- **Minimal base, expressive details.** Clean silhouettes with thin lines are still the norm. The motion goes into the accents: hover pulses, particles, shape changes. ([custom-cursors.com trends 2026](https://custom-cursors.com/blog/custom-cursors-ui-design-trends-2026), [evolution 2025–26](https://custom-cursors.com/blog/arrow-to-animation-evolution-cursors-2025-2026))
- **The cursor as branding.** Smart cursors that change shape or leave an animated trail now act as identity elements. ([UX/UI trends 2026](https://uxuidesignerblog.journoportfolio.com/articles/ux-ui-trends-2026))
- **Neon glow trails are the dominant "stylish" effect.** Common controls are glow intensity, softness and neon colour presets. ([Framer glow-trail cursor](https://www.framer.com/marketplace/components/glow-trail-cursor/), [neon cursor](https://www.framer.com/community/marketplace/components/neon-cursor/), [Speckyboy cursor effects](https://speckyboy.com/css-javascript-cursor-effects.md))
- **Click feedback.** Ripples and particle bursts are standard, with adjustable colour, size and opacity. ([cursor-style-animation demo](https://cdn.jsdelivr.net/wp/cursor-style-animation/trunk/demo.html))
- **Accessibility.** Motion must be optional (offer a reduce-motion mode), effects must never block clicks, and trails stay short. ([Smashing: reduced motion](https://shop.smashingmagazine.com/2020/09/design-reduced-motion-sensitivities/), [tempertemper: reducing motion](https://www.tempertemper.net/blog/reducing-motion), [cursor-effects](https://awesome.ecosyste.ms/projects/github.com%2Ftholman%2Fcursor-effects))

### Effect catalog considered

| Effect | Verdict |
|---|---|
| Neon tube outline + glow | ✅ Core look of every cursor (signed-distance-field neon) |
| Shimmer sweep across the cursor | ✅ Arrow / Up-arrow (Full motion) |
| Heartbeat LED (lub-dub) | ✅ Signature detail and the reason for the name "BioPulse" |
| Comet trail | ✅ Overlay |
| DNA-helix trail | ✅ Overlay, a signature effect unique to this brand |
| Gold-dust particles | ✅ Overlay (taken from the "That line is made of GOLD" post) |
| Click ripple + sparks | ✅ Overlay (left = neon, right = accent) |
| Morphing / magnetic snap | ❌ Not possible system-wide (Windows gives no hover info from other apps) |
| Constant spin on the arrow | ❌ Distracting during work. A rest-heavy loop is used instead |

## 3. Technical constraints (Windows 11)

- Only **.cur** (static) and **.ani** (animated) work as system pointers. ([ANI format](https://en.wikipedia.org/wiki/ANI_(file_format)), [gdgsoft ANI](https://www.gdgsoft.com/anituner/help/aniformat.htm))
- **.ani** is a RIFF `ACON` container: an `anih` header (36 bytes, rate in 1/60 s jiffies, AF_ICON flag), optional `rate`/`seq ` chunks, and a `LIST fram` of `icon` chunks. Each `icon` chunk is a full .cur file.
- Windows' own `aero_arrow.cur` embeds 32/48/64/96/128 px BMP-32bpp images (checked locally). Windows picks the size from `CursorBaseSize` × display scale, so the generator writes all 5 sizes. ([ElevenForum: cursor size](https://www.elevenforum.com/t/reloading-mouse-cursors-and-changing-their-size.37551/latest), [wisechecker: pointer size](https://wisechecker.com/increase-pointer-size-without-losing-theme-customization/))
- Scheme = per-user registry `HKCU\Control Panel\Cursors` (17 roles, including Pin and Person on Win 11) plus `Schemes`. Applied live with `SystemParametersInfo(SPI_SETCURSORS)`, with no admin rights needed. ([MS DevBlogs: pointer scheme](https://devblogs.microsoft.com/scripting/use-powershell-to-change-the-mouse-pointer-scheme/), [MS TechCommunity](https://techcommunity.microsoft.com/discussions/windows11/how-do-i-batch-change-cursor/4436080))
- Trails, ripples and particles cannot live inside cursor files, so they need a click-through overlay. It is a layered, topmost, `WS_EX_TRANSPARENT` window that only covers the effect's bounding box, so it stays cheap and goes idle when nothing moves.

## 4. Design decisions

1. **Renderer.** Every glyph is a vector mask → signed distance field → neon shading: dark glass core, inner rim light, white-hot tube edge and exponential outer glow, supersampled. One look across all 17 roles in any palette.
2. **Motion levels.** *Full* (shimmer + heartbeat), *Subtle* (heartbeat only, long rests), *Off* (static .cur). Busy/working spinners always animate because they show system state.
3. **Moods** (presets, all editable):

| Mood | Use for | Neon | Accent |
|---|---|---|---|
| Signature · Bio AI | default / creating | cyan `#22D3EE` | amber `#FFB020` |
| Business · Royal | meetings, pitches | royal `#3B6BFF` | ice `#E9F0FF` |
| Deep Focus | long work blocks (Subtle motion, no trail) | slate `#8FA3BF` | mint `#5EEAD4` |
| Research · Germ | reading papers, analysis | green `#34D27B` | gold `#FFC53D` |
| Deadline · Alert | crunch mode | red `#FF3B4E` | white |
| Gold Hour | wins, celebration | gold `#FFC23D` | orange `#FF7A1A` |
| Infrared · Creative | design, editing reels | violet `#B46CFF` | pink `#FF4FA3` |
| Clinical · Light | light-mode apps, docs | sky `#0EA5E9` | amber `#F59E0B` (light core) |
| Night Owl | late sessions | teal `#2DD4BF` | lavender `#A78BFA` |

4. **Safety.** The current scheme is backed up before the first apply, and *Restore Windows cursors* puts it back exactly. Everything is per-user and nothing needs admin rights.
5. **Added requests from Akash (during the build):**
   - Movement draws a shooting star with a tail that sheds **glowing ferrocene molecules**: two counter-rotating Cp pentagons around a glowing Fe atom.
   - Loading is a **DNA spindle**: a tapered helix rotates while base pairs bind in a travelling wave.
   - **Left click = Synapse** (a neural burst). **Right click = Molecule** (a benzene ring with Kekulé bonds).
   - Bio-AI motifs appear on every glyph.
6. **Found while testing:** Windows' ANI loader rejects frames that hold more than **4 images**. Any 4 sizes load, 5 fail. Animated cursors therefore embed 32/48/64/128 px, and static `.cur` files keep all five sizes.
7. **v2 additions:**
   - A **Fluid** shape inspired by modern agent-style pointers: tail-less, smooth, curvy, with a solid body, white rim and soft shadow. The curves come from morphological rounding on the distance field.
   - New Bio-AI effects: neural constellation, ECG pulse, agent glide, Atom and Mitosis clicks, double-click mitosis, a drag tether and an idle orbit.

8. **v3 (Myelin):** the user asked for "liquid" pointers and an Apple-like app. Pointers became Liquid Glass (Clear or Tinted), shaded with the distance-field gradient as the surface normal. The UI became one configurator page with three premium looks, and it follows impeccable's Operate-mode guidance: one sans-serif family, the accent only on actions and selection, no eyebrow labels, no glow halos.
