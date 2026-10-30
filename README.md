# Clio

Desktop sticky notes for Windows, built as a companion to
[Argus](https://github.com/XRTC5/argus). Argus is the watcher with a hundred
eyes. Clio is the muse of history, the one who keeps the record.

- **Notes stay where you leave them.** Drag a note by its header and resize it
  from any edge. Its exact rectangle is restored after a restart, in physical
  pixels, so it stays correct across displays with different scale factors.
- **One hub.** Click the hub to stow every note into it, and click again to
  release them all to their places. A stowed note has no window, so it costs
  nothing but a line of JSON.
- **`+` on every note** opens a new note beside it, so you never have to go
  back to the hub.
- **Formatting.** `Aa` opens a format bar with font, size, bold, italic,
  underline, strikethrough, superscript, subscript, lists, and a grid of maths
  and special characters. The Google Docs shortcuts work too (see below).
- **Doodle.** The pen button draws over a note. There are five pen colours and
  a stroke eraser.
- **`/todo`** turns into a checkbox. Click the box to tick it.
- **Erase** asks first, then the note dissolves into **History**. Erased notes
  are kept for 5 days by default (change it in Settings), then deleted. Open
  History to restore one or delete it for good.
- **Settings** (the gear on the hub) has Launch at start, Keep notes on top,
  and the History retention. Launch at start uses a logon task, the same as
  Argus.
- **Desktop layer by default**, the same as Argus: other windows open over
  your notes. Choose *Keep notes on top* from the hub's right-click menu or the
  tray menu to keep them above your work instead.

Shortcuts inside a note:

| Keys | Does |
|---|---|
| `Ctrl+B` / `Ctrl+I` / `Ctrl+U` | bold / italic / underline |
| `Alt+Shift+5` or `Ctrl+Shift+X` | strikethrough |
| `Ctrl+.` / `Ctrl+,` | superscript / subscript |
| `Ctrl+Shift+.` / `Ctrl+Shift+,` | larger / smaller text |
| `Ctrl+Shift+7` / `Ctrl+Shift+8` | numbered / bulleted list |
| `Ctrl+Shift+L` / `E` / `R` / `J` | align left / centre / right / justify |
| `Ctrl+\` | clear formatting |
| `Ctrl+N` | new note beside this one |
| `Ctrl+D` | doodle |
| `Esc` | leave doodle mode, or cancel an erase |

`Clio.exe --quit` asks a running Clio to save and exit. Argus uses this for
its Stop button.

## Build

```powershell
./build.ps1 -Run
```

Needs the .NET 9 SDK. Notes are stored in `%APPDATA%\Clio\notes.json`.

## Footprint

Clio uses the same approach as Argus: software rendering, workstation
non-concurrent GC, no WinForms, and a working-set trim once startup settles
and after each stow or erase. Measured with two notes open: about 35 MB
private and 4-6 MB working set at idle.
