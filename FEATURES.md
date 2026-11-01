# Clio feature checklist

Every feature, however small, and whether it is confirmed working.

- **[x]** confirmed: by an automated test on an isolated copy (**test**), by RTC by hand (**RTC**), or both
- **[ ]** not yet confirmed: usually needs a real keyboard, a mouse drag or the real pointer, which
  the automated tests cannot produce. Try it and tick it.

Last updated 2026-11-01, after RTC's first full hand-test. Full automated suite: 11 tests, all passing.

## Notes and the hub

- [x] Notes stay exactly where you leave them, restored after a restart (test, RTC)
- [x] Drag a note by its header (RTC)
- [x] Resize a note from any edge (RTC)
- [x] Resize grip in the bottom-right corner (test: hit area; RTC)
- [x] Notes cannot snap to full screen when dragged to the top (test: no maximise box)
- [x] `+` on a note opens a new note beside it (test)
- [x] Hub `+` makes a new note above the hub (test)
- [x] Click the hub to stow every note into it (test)
- [x] Click again to release them all to their places (test)
- [x] Hub shows the number of notes, out or stowed (test)
- [x] Notes and the hub keep their size when moved between the 250% and 175% displays (test)
- [x] Header shows the record number and the date and time the note was made (test)
- [x] Hover the date for the year and the last edit time (test)

## Window layering

- [x] Click a note to bring it to the front; click another window to send it back (RTC)
- [x] Keep notes on top: the triple-arrow button on the hub (test)
- [x] Keep notes on top: the switch in Settings (test)
- [x] Keep notes on top: the hub's right-click menu (test)
- [x] The saved on-top setting applies at startup (test)
- [x] Lock one note on top: the padlock in the colour dropdown (test, RTC)
- [x] A locked note shows a small lock in place of the accent bar (test)

## Look

- [x] Five accent colours, picked from the colour dropdown (test)
- [x] See-through body: the lens in the colour dropdown (test, RTC)
- [x] See-through keeps the header, border, brackets and text solid (test)
- [x] Clicks still land on a see-through note (test: alpha 1/255)

## Text

- [x] Rich text editor (test)
- [x] Notes from before rich text load correctly (test)
- [x] Font picker (test)
- [x] Font size picker (test)
- [x] Bold, italic, underline, strikethrough buttons (test)
- [x] Superscript and subscript buttons (test)
- [x] Bulleted list button (test)
- [x] Numbered list button (test)
- [x] Checklist button (test)
- [x] Clear formatting button (test)
- [x] Maths and special characters grid (test)
- [x] `/todo` becomes a checkbox, several at once too (test)
- [x] Click a checkbox to tick it (RTC)
- [x] Paste keeps formatting; a pasted image never lands inside the text (test)

### Text: list markers follow the text size

- [x] Numbers grow and shrink with the text size (test)
- [x] Bullets grow and shrink with the text size (test)
- [x] Checkboxes grow and shrink with the text size, even if only the words were selected (test)
- [x] The list indent widens so big numbers are not cut off (test)
- [x] Superscript inside a list does not shrink the number (by design; not separately tested)

### Text: Google Docs keys

- [x] Ctrl+B, Ctrl+I, Ctrl+U (RTC)
- [x] Alt+Shift+5 or Ctrl+Shift+X strikethrough (RTC)
- [x] Ctrl+. and Ctrl+, superscript and subscript (RTC)
- [x] Ctrl+Shift+. and Ctrl+Shift+, bigger and smaller (RTC)
- [x] Ctrl+Shift+7, 8, 9 numbered list, bullets, checklist (RTC)
- [x] Ctrl+Shift+L, E, R, J alignment (RTC)
- [x] Ctrl+\ clear formatting (RTC)
- [x] Enter on a checklist line adds a new box (RTC)
- [x] Enter on a bullet or number continues the list (RTC)
- [x] Enter on an empty item ends the list (RTC)

## Undo and redo (rebuilt in `a19086f`)

- [ ] Ctrl+Z undoes the last word typed, not the whole burst
- [ ] The caret stays where the change was
- [ ] Ctrl+Y and Ctrl+Shift+Z redo after an undo
- [ ] Formatting, deleting and pasting undo as their own steps
- [ ] Undo a picture move, resize, restack, paste or removal
- [ ] Undo a whole doodle session (after Done)
- [ ] Inside a doodle session, Ctrl+Z takes back the last stroke
- [ ] Inside a doodle session, Ctrl+Z brings back what the eraser removed
- [ ] Inside a doodle session, Ctrl+Y redoes
- [ ] Click REC to show the arrows; they stay open while you click them
- [ ] Arrows grey out when there is nothing to undo or redo
- [ ] History survives switching to another window for a few minutes

## Doodling

- [x] Pen button or Ctrl+D starts doodling (test, RTC)
- [x] Drawing works over the text (test: hit-test; RTC)
- [x] Five pen colours (test)
- [x] Stroke eraser (RTC)
- [x] Clear and Done buttons (test)
- [x] Each doodle session becomes one drawing (test)
- [x] Older notes' doodles become one drawing (test)
- [x] Saving mid-doodle keeps the strokes (by design: Flush splits the session)

## Advanced Features (Settings)

- [x] Advanced Features is a dropdown: the chevron shows the ticks under it (test)
- [x] The master switch opens the dropdown when turned on (test)
- [x] The ticks under it are greyed until the master is on (test)
- [x] Off: no picture pasting, nothing to drag or restack, no Alt behaviour, no four-arrow button (test)
- [x] Off: existing pictures and drawings stay visible but locked (test)

### Multi-select (the master switch)

- [ ] Alt+click an item, keep Alt held, Alt+click another: both selected, they move together
- [ ] Let go of Alt, then Alt+click something else: a fresh selection
- [ ] Alt+click a selected item to take it out (fixed 2026-11-01: a tiny wobble during the click counted as a drag)
- [ ] Alt+drag over empty space draws a box; what it touches is selected
- [ ] Box and lasso select text boxes too (with Free Drag Writing on)
- [ ] No need to keep holding Alt or the mouse; the selection stays
- [ ] A click elsewhere on the note, or Esc, clears the selection
- [ ] Dragging one selected item moves them all
- [ ] Right-click a selected item: restack or delete all of them

### Images (tick)

- [x] Images tick turns picture features on (test)
- [ ] Ctrl+V pastes a picture (screenshot, browser image, image file)
- [x] Copied text still pastes as text (by design: text wins)
- [x] Pictures are saved as files and survive restarts (test)
- [x] Drag a picture (RTC)
- [ ] Resize a picture from its corner, keeping its shape
- [x] Right-click: Bring forward, Bring to front, Send backward, Send to back (test)
- [x] Right-click: Remove picture (test)
- [x] Options grey out when there is nowhere to go (test)
- [x] Default order: pictures at the back, then drawings, then writing (test)
- [x] A new paste lands on top (by design)
- [x] Pictures behind the text answer a plain click away from the letters (RTC)
- [x] Alt+drag grabs a picture even over the letters (RTC)
- [ ] Alt+right-click opens the picture menu, and it stays open
- [x] Arrange mode (four arrows) appears only on notes with items (test)
- [ ] Arrange mode: drag and right-click items behind the text
- [ ] Drag a drawing
- [ ] Right-click a drawing: restack or delete it

### Free Drag Writing (tick)

- [x] Free drag writing tick in the Advanced dropdown (test)
- [ ] Click empty space to start a text box there
- [x] Turning Free Drag Writing on makes the writing already on the note a text box, so it drags too (test)
- [x] That box keeps the writing's place in the stack, and nothing is duplicated (test)
- [ ] Text inside a box can be clicked and selected even where a picture or drawing sits behind it
- [x] Text boxes are saved and come back after a restart (test)
- [x] Text boxes stay editable with the tick off; they just do not move (test)
- [x] The Aa bar and keys act on whichever box you are typing in (test: bold went to the box only)
- [x] An empty box removes itself when you click away (test)
- [ ] Drag a box by the strip along its top
- [ ] Widen a box from its bottom-right corner
- [ ] Right-click inside a box: Cut, Copy, Paste, stacking, Delete text box
- [ ] Alt+click a box to add it to a multi-selection
- [ ] Undo and redo inside a box

### Fixed after RTC's hand-test (2026-11-01)

- [x] Clicking a sleeping note brings it to the front (test)
- [ ] Picture resize: a grip is drawn in the corner on hover and while selected; drag it

### Lasso select (tick)

- [x] Lasso select tick in the Advanced dropdown, saved (test)
- [ ] Alt+Shift+drag over empty space draws a loop; what it touches is selected
- [ ] Plain Alt+drag still draws the box

### Grouping (tick)

- [x] Grouping tick in the Advanced dropdown, saved (test)
- [x] Groups are saved and survive a restart (test)
- [ ] Select several items, right-click, Group
- [ ] Clicking any member selects the whole group
- [ ] Dragging one member moves the whole group
- [ ] Right-click a member, Ungroup
- [ ] Deleting a grouped item leaves the rest of the group intact
- [ ] Group and Ungroup are undoable
- [ ] Grouping off: members act on their own, and the groups are kept
- [ ] Works across kinds: pictures, drawings and text boxes in one group

## History and settings

- [x] Erase asks first, then the note dissolves (test, RTC)
- [x] Erased notes go to History, kept 5 days by default (test)
- [x] History retention is adjustable from 1 to 90 days (test)
- [x] Restore a note from History, formatting and all (test)
- [x] Delete from History for good; Empty History needs two presses (test)
- [x] Expired History entries are purged at start and hourly (test)
- [x] Launch at start, on and off (test)

## Hide and memory

- [x] Hide Clio from the hub or tray menu (test)
- [x] Double-click the tray icon to bring it back (test)
- [x] A hidden Clio closes every note window, so it uses almost no memory (test)
- [x] Idle notes sleep as pictures after 15 s (5 min if they have undo history), about 0.55 MB each (test, re-measured)
- [x] Hover a sleeping note to wake it (RTC)

## With Argus

- [x] Argus finds Clio by itself when both are installed (test)
- [x] Argus Stop asks Clio to save and quit (test)
- [x] Argus Start opens Clio (test)
- [x] Argus shows Clio's RAM (test)

## Releases

- [x] v1.0.0 on GitHub with a small zip and a standalone zip (test)
