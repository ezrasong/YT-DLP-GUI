## yt-dlp GUI Downloader

A modern, dark-mode graphical interface for **yt-dlp**, designed for Windows. Easily download videos or extract audio in MP3 format. Features automatic updates for the yt-dlp backend, a download queue, progress tracking, and a polished, responsive UI.

---

## Features

* **Dark-themed UI** using Tkinter + ttkbootstrap for a sleek look.
* **Automatic update** check and installation for yt-dlp on launch.
* **Download Formats**:

  * Video (MP4)
  * Audio (MP3)
* **Download Queue**: Add multiple URLs, start all, and clear completed jobs.
* **Progress Tracking**: Live percentage updates per job.
* **Responsive Layout**: Scales and centers controls for any window size.
* **Custom Icon**: Displays a downloads-style icon in the title bar and taskbar.

---

## Prerequisites

* **Python 3.8+** installed on Windows
* **yt-dlp**
* **ttkbootstrap**
* **FFmpeg** in your PATH (required for MP3 conversion)
* **download.ico** in `images/download.ico` for the app icon

## Installation

1. Clone or download this repository:

   ```bash
   git clone https://github.com/ezrasong/YT-DLP-GUI.git
   cd YT-DLP-GUI
   ```
2. Install Python dependencies:

   ```bash
   pip install yt-dlp ttkbootstrap
   ```
3. Ensure `ffmpeg.exe` is available on your system PATH for audio extraction.

---

## Running the App

Simply launch with:

```bash
python app.py
```

The application window will appear, auto-checking for yt-dlp updates.

---

## Packaging as a Windows Executable

To create a single `.exe` file with a custom icon, use **PyInstaller**:

1. Install PyInstaller:

   ```bash
   pip install pyinstaller
   ```
2. From the project root, run:

   ```powershell
   pyinstaller --onefile --windowed --icon=images/download.ico app.py
   ```
3. Find `yt_dlp_gui.exe` in the `dist/` folder.

> **Tip**: If `pyinstaller` is not recognized, invoke via Python:
>
> ```powershell
> py -m PyInstaller --onefile --windowed --icon=images/download.ico app.py
> ```

---

## Usage

1. **Enter** the video URL in the top field.
2. **Select** format (Video MP4 or Audio MP3).
3. **Choose** download folder or accept the default.
4. **Add to Queue**, then **Start All**.
5. **Monitor** progress in the table below.
6. **Clear Completed** when done.

