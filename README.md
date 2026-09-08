# EncodeKit

EncodeKit is a free, open-source, portable Windows batch video converter and FFmpeg GUI. Queue MP4, MKV, and other video files, compress them for personal use, or YouTube with AV1 or NVIDIA NVENC support, convert formats, strip audio, extract JPEG frames, and let encodes run in the background — no installer. Presets live in a simple `.ini` file, so you can tweak the built-in profiles or add your own.

---

## What it is

EncodeKit is a small WinForms front-end around `ffmpeg.exe`. You keep your own presets in a plain `presets.ini`. The app:

- Queues many files
- Builds the FFmpeg command from the selected preset
- Runs them one after another
- Shows progress, ETA, CPU/RAM, and a live log
- Writes next to the source file, or into a folder you choose
- On every run, it creates a shortcut for itself in "Send To" for easy access for any files to be added using context menu.
- On every launch, EncodeKit writes a shortcut into the Windows **Send to** menu so you can right-click any file and add it to the open queue.

It is meant to live on a USB stick or a tools folder: copy the directory, drop in FFmpeg, run.

---

## Layout

```
EncodeKit.exe
assets\
  EncodeKit.cs
  presets.ini
  settings.ini
  ffmpeg.exe        (included)
  ffprobe.exe      (included)
  app.ico          (optional)
  last_preset.txt  (created at runtime)
  dest.txt         (created at runtime)
```

`ffmpeg.exe` and `ffprobe.exe` are not bundled. Use a recent Windows GPL build (BtbN / gyan.dev both work). Put both binaries in `assets`.

---

## Usage

### 1. Add files

- **Add** button, or
- Drag videos onto the window, or
- Drag a folder (every file inside is queued), or
- Windows **Send to → EncodeKit** (a shortcut is created on first launch). A second Send to adds files to the already-open queue instead of starting another instance.

Duplicates are skipped. Size and duration are probed with `ffprobe`.

### 2. Pick a preset

The dropdown lists every section in `assets\presets.ini`. NVIDIA / CUDA presets are hidden automatically if CUDA is not available. The last used preset is remembered.

Included starting points:

| Preset | What it does |
|---|---|
| NVIDIA / CPU YouTube-like MP4 | AV1 at 720p, 1080p, 1440p, original size, 16:9 crop, 9:16 portrait |
| MP3 audio only | First audio track → high-quality VBR MP3 |
| JPEG 1 frame per second | One JPEG every second into `videoname-frames\` |
| Remove audio (copy video) | Bitstream copy, drop all audio, keep the original container |

Hover / read the note under the dropdown for the short description of the current preset.

### 3. Choose where output goes

- **Same as source** — `Clip.mp4` becomes `Clip.1080p.mp4` next to the original. JPEG jobs become `Clip-frames\0001.jpeg`, `0002.jpeg`, …
- **Custom Directory** — same file names, written into the folder you select. EncodeKit creates missing folders (needed for the JPEG sequence).

### 4. Run the queue

| Button | Action |
|---|---|
| Start | Encode everything still Waiting. Jobs already Done are skipped. |
| Restart All | Reset every job and encode from the top. |
| Stop | Kill the current ffmpeg process. The job is marked Stopped. |
| Remove / Clear | Edit the queue while idle. Drag rows to reorder. |

While a job runs you get elapsed time, ETA, speed, a percent bar, and the ffmpeg log.

### 5. After a job finishes

- **Source → Open** — Explorer selects the original file.
- **Converted** — a button with the output size and the change vs the source, e.g. `12.4 MB (-64%)`.
  - Green if the file got smaller, red if it got larger.
  - Click opens the output file, or the frames **folder** for JPEG sequences.

### 6. Presets (`assets\presets.ini`)

Each section is one preset. Fields:

```ini
[My preset name]
note=Short description shown under the dropdown.
pre=-hwaccel cuda -hwaccel_output_format cuda
args=-c:v av1_nvenc ... -c:a aac -b:a 160k
suffix=.mysuffix
ext=mp4
```

- `pre` — flags **before** `-i` (hardware decode, etc.). Leave empty for CPU.
- `args` — flags **after** `-i`. This is the actual encode.
- `suffix` — inserted before the extension. Use `-frames\%04d` for an image sequence.
- `ext` — output extension. Use `*` to keep the source container (mute / remux jobs).

CUDA is detected from `pre` / `args` (`cuda` or `nvenc`). Those presets hide themselves when the GPU cannot run them.

Reload settings after you edit the ini (or restart the app).

### 7. Settings (`assets\settings.ini`)

```ini
cpu_percent=75
priority=BelowNormal
```

- `cpu_percent` (1–100) — cap FFmpeg (and SVT-AV1 `lp=`) at this fraction of logical processors.
- `priority` — Windows priority for `ffmpeg.exe`: `Idle`, `BelowNormal`, `Normal`, `AboveNormal`, `High`. BelowNormal keeps the desktop usable during long encodes.

---

## Why use it

- **Batch, not one file at a time.** Queue a folder, walk away.
- **Presets you own.** The command is an ini file, not buried in a GUI. Copy, tweak, share.
- **Video is not re-encoded unless you ask.** Mute is `-c copy -an`. JPEG dump is a frame extract. YouTube-like presets are the ones that actually compress.
- **GPU when you have it, CPU when you don’t.** Same list, CUDA rows disappear on machines without NVIDIA.
- **Portable.** No installer, no registry, no `%APPDATA%` requirement. The exe + `assets` folder is the whole app.
- **The desktop stays usable.** Thread cap + BelowNormal ffmpeg so a 4-hour AV1 job does not freeze Explorer.
- **Send To.** Right-click a clip in Explorer, it lands in the open queue.
- **You can see the command.** The log prints the exact ffmpeg line so you can debug or paste it elsewhere.

---

## Build from source

You need:

- Windows 10/11 x64
- .NET Framework 4.x (the `csc.exe` that ships with Windows / the 4.8 Developer Pack is enough — Visual Studio is optional)
- This folder: `EncodeKit.cs`, `presets.ini`, `settings.ini`, `build.bat`
- `ffmpeg.exe` and `ffprobe.exe` (GPL Static build preferred) to actually encode (included in source/assets folder)

### Seteps

- Download Source Folder, then run the build.bat file, this should be the content of the bat file.
- You can change the app icon as needed. the build uses app.ico by default included in the assets folder

```bat
@echo off
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo C# compiler not found.
  pause
  exit /b 1
)
if not exist "assets\EncodeKit.cs" (
  echo assets\EncodeKit.cs not found.
  pause
  exit /b 1
)
if not exist "assets\app.ico" (
  echo assets\app.ico not found.
  pause
  exit /b 1
)
"%CSC%" /nologo /optimize+ /target:winexe /out:EncodeKit.exe /win32icon:assets\app.ico /r:System.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll assets\EncodeKit.cs
if errorlevel 1 (
  echo Build failed.
  pause
  exit /b 1
)
echo Built EncodeKit.exe
pause
```

If `csc.exe` is missing, install the .NET Framework 4.8 Developer Pack (or Visual Studio with the .NET desktop workload) and run `build.bat` again.

There are no NuGet packages and no project file. One `.cs` file in, one `.exe` out.
```
