# Clio

Desktop sticky notes for Windows, built as a companion to
[Argus](https://github.com/XRTC5/argus). Argus is the watcher with a hundred
eyes. Clio is the muse of history, the one who keeps the record.

- **Notes stay where you leave them.** Drag a note by its header and resize it
  from any edge or the grip in the bottom-right corner. Its exact rectangle is restored after a restart, in physical
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
- **Click a note to bring it forward**, the way any window works: click Chrome
  and Chrome comes back in front. Turn on *Keep notes on top* (Settings, or the
  hub's right-click menu) to keep notes above everything while you work.
- **Lists behave like Docs.** Enter on a checklist line starts a new unticked
  box, and on a bulleted or numbered line it continues the list. Enter on an
  empty item ends the list.

## Advanced Features

Off by default. Turn them on in Settings, Advanced features (the chevron opens
the list):

- **Multi-select** (the main switch): Alt+click items to select several, keeping
  Alt held between clicks, or Alt+drag a box around them. Drag one to move them
  all, and right-click to restack or delete them all.
- **Images**: paste pictures with Ctrl+V. Pictures and drawings (each doodle
  session is one drawing) can be dragged and moved in front of or behind the
  text.
- **Free drag writing**: click empty space to start a text box there; drag it by
  its top edge, widen it from its corner, and right-click it for its menu. Turning
  this on makes the writing already on a note a box too.
- **Lasso select**: Alt+Shift+drag a loop around items.
- **Grouping**: select items, right-click, Group; they then select and move as
  one.
- **Colours+**: the note colour dropdown becomes a full palette, and the doodle
  bar gains a + button that opens it for the pen.

Undo and redo work as in Word: Ctrl+Z, Ctrl+Y, or click a note's REC number for
the arrows. Every feature and whether it is confirmed working:
[FEATURES.md](FEATURES.md).

## Install

1. Download one zip from the [latest release](https://github.com/XRTC5/clio/releases/latest):
   - **`clio-1.3.0-win-x64-standalone.zip`** (51 MB): a single `Clio.exe` with
     .NET inside. Nothing else to install.
   - **`clio-1.3.0-win-x64.zip`** (0.2 MB): needs Microsoft's .NET 9 Desktop
     Runtime, installed once with `winget install Microsoft.DotNet.DesktopRuntime.9`.
     Choose this one if you also run [Argus](https://github.com/XRTC5/argus):
     both apps then share one copy of .NET in memory, and runtime security fixes
     arrive through Windows Update.
2. Unzip it to a folder you will keep, for example `%LOCALAPPDATA%\Clio`.
3. Run `Clio.exe`. It starts with Windows by default; you can turn that off in its
   settings.

Windows SmartScreen may warn on first run because the exe is not code-signed.
Choose **More info**, then **Run anyway**.

Shortcuts inside a note:

| Keys | Does |
|---|---|
| `Ctrl+B` / `Ctrl+I` / `Ctrl+U` | bold / italic / underline |
| `Alt+Shift+5` or `Ctrl+Shift+X` | strikethrough |
| `Ctrl+.` / `Ctrl+,` | superscript / subscript |
| `Ctrl+Shift+.` / `Ctrl+Shift+,` | larger / smaller text |
| `Ctrl+Shift+7` / `Ctrl+Shift+8` / `Ctrl+Shift+9` | numbered list / bulleted list / checklist |
| `Tab` / `Shift+Tab` on a list line | indent / outdent (numbers go 1.1, 1.2.1 like Docs) |
| `Ctrl+Shift+L` / `E` / `R` / `J` | align left / centre / right / justify |
| `Ctrl+\` | clear formatting |
| `Ctrl+N` | new note beside this one |
| `Ctrl+D` | doodle |
| `Esc` | leave doodle mode, or cancel an erase |

## Updating

Quit Clio, unzip the new release over the old folder, and start it again. Your
notes are kept in `%APPDATA%\Clio`, apart from the program, so they carry over.
A dot on the hub means a newer release is out; click it for the download page.
Clio checks GitHub once per start and never downloads anything by itself. Turn
the check off in Settings.

## With Argus

Clio is a separate download from [Argus](https://github.com/XRTC5/argus), and
each works without the other. If both are installed, they find each other with
no setup. Each time Clio starts it writes its path to
`%APPDATA%\Clio\app.json`, and Argus shows a **Clio** row with Start and Stop.
Argus's Stop runs `Clio.exe --quit`, which asks the running Clio to save and
exit, so it never kills Clio partway through a save.

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
