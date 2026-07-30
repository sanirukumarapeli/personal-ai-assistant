# Documents — how to use

Local files for your personal assistant (no cloud Drive).

## Folders (under repo `data/`)

| Folder | Purpose |
|---|---|
| `notes/` | Markdown notes (`.md`) |
| `pdfs/` | Generated PDFs |
| `office/` | Generated Word (`.docx`) / Excel (`.xlsx`) / PowerPoint (`.pptx`) |
| `uploads/` | Files you upload from the web UI |

`data/` is gitignored.

## Web UI

1. Run API + web as usual.
2. Use the **paperclip** to upload `.pdf`, `.docx`, `.xlsx`, or `.pptx`.
3. Ask in chat, e.g. `Summarize lease.pdf` or `What does expenses.xlsx say about rent?`
4. Download links in replies are clickable (`/v1/documents/...`).

## Example prompts

- Save a note titled Grocery list with milk and eggs  
- Make a one-page PDF trip checklist…  
- Write a Word letter to my landlord about a repair…  
- Make an Excel sheet of monthly expenses with columns Category, Amount…  
- Make a 5-slide PowerPoint about our product roadmap…  
- Summarize [uploaded-file].pdf  
- Extract the text from report.docx / pitch.pptx  
- Soften the tone of lease.docx and overwrite it (then confirm)  
- Save a revised copy of checklist.pdf as checklist-v2.pdf  

## Safety

- **Overwrite / rewrite** of an existing file needs your clear confirmation (`user_confirmed` / Confirm button).
- Prefer **save as a new name** when you want to keep the original.
- **Delete note** also requires confirmation.
- PDF / PowerPoint “edit” regenerates the file from new text/slides (not a pixel / designer editor).
- Scanned image-only PDFs need OCR (not in v1).

## API (optional)

- `GET /v1/documents` — inventory  
- `GET /v1/documents/{notes|pdfs|office|uploads}/{name}` — download  
- `POST /v1/documents/upload` — multipart field `file`
