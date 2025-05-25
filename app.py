# app.py
# A Windows GUI for yt-dlp with dark mode, queue, cancel, and auto-update
# Requires: pip install yt-dlp ttkbootstrap

import os
import sys
import threading
import subprocess
import tkinter as tk
from tkinter import filedialog, messagebox
import ttkbootstrap as tb
from ttkbootstrap.constants import *
from yt_dlp import YoutubeDL


def resource_path(relative_path):
    """
    Get absolute path to resource, works for dev and for PyInstaller bundles.
    """
    try:
        base_path = sys._MEIPASS
    except AttributeError:
        base_path = os.path.abspath('.')
    return os.path.join(base_path, relative_path)


class YTDL_GUI(tb.Window):
    def __init__(self):
        super().__init__(themename='darkly', title='yt-dlp Downloader', size=(800, 600))
        # Set window icon
        try:
            self.iconbitmap(resource_path('images/download.ico'))
        except Exception:
            pass

        self.queue = []
        self.create_widgets()
        threading.Thread(target=self.auto_update, daemon=True).start()

    def create_widgets(self):
        # Layout configuration
        self.columnconfigure(0, weight=1)
        self.columnconfigure(1, weight=0)
        self.columnconfigure(2, weight=1)
        self.rowconfigure(0, weight=0)
        self.rowconfigure(1, weight=1)

        # Centered control panel
        ctrl = tb.Frame(self, padding=20)
        ctrl.grid(row=0, column=1, sticky='ew')
        ctrl.columnconfigure(0, weight=0)
        ctrl.columnconfigure(1, weight=1)

        pad = {'padx': 8, 'pady': 6}
        # URL entry
        tb.Label(ctrl, text='🔗 Video URL:', bootstyle=INFO).grid(row=0, column=0, sticky=E, **pad)
        self.url_var = tk.StringVar()
        tb.Entry(ctrl, textvariable=self.url_var).grid(row=0, column=1, sticky=EW, **pad)

        # Format dropdown
        tb.Label(ctrl, text='🎵 / 🎬 Format:', bootstyle=INFO).grid(row=1, column=0, sticky=E, **pad)
        self.format_box = tb.Combobox(ctrl, values=['Video (MP4)', 'Audio (MP3)'], state='readonly')
        self.format_box.current(0)
        self.format_box.grid(row=1, column=1, sticky=EW, **pad)

        # Save directory picker
        tb.Label(ctrl, text='📁 Save to:', bootstyle=INFO).grid(row=2, column=0, sticky=E, **pad)
        path_frame = tb.Frame(ctrl)
        path_frame.grid(row=2, column=1, sticky=EW, **pad)
        path_frame.columnconfigure(0, weight=1)
        self.dir_var = tk.StringVar(value=os.path.expanduser('~/Downloads'))
        tb.Entry(path_frame, textvariable=self.dir_var).grid(row=0, column=0, sticky=EW)
        tb.Button(path_frame, text='Browse', command=self.browse_folder, bootstyle=SECONDARY).grid(row=0, column=1, padx=(5,0))

        # Action buttons
        action = tb.Frame(ctrl)
        action.grid(row=3, column=0, columnspan=2, pady=(10,20))
        btns = [('Add to Queue', PRIMARY, self.add_to_queue),
                ('Start All', SUCCESS, self.start_all),
                ('Cancel Selected', WARNING, self.cancel_selected),
                ('Clear Completed', DANGER, self.clear_completed)]
        for text, style, cmd in btns:
            tb.Button(action, text=text, command=cmd, bootstyle=style).pack(side=LEFT, padx=6)

        # Queue display
        cols = ('URL', 'Format', 'Status', 'Progress')
        self.tree = tb.Treeview(self, columns=cols, show='headings', bootstyle='secondary')
        specs = {
            'URL': {'anchor':W, 'width':300},
            'Format': {'anchor':CENTER, 'width':100},
            'Status': {'anchor':CENTER, 'width':100},
            'Progress': {'anchor':CENTER, 'width':80},
        }
        for col in cols:
            self.tree.heading(col, text=col)
            self.tree.column(col, **specs[col])
        self.tree.grid(row=1, column=0, columnspan=3, sticky='nsew', padx=20, pady=(0,20))

    def browse_folder(self):
        folder = filedialog.askdirectory(initialdir=self.dir_var.get())
        if folder:
            self.dir_var.set(folder)

    def add_to_queue(self):
        url = self.url_var.get().strip()
        fmt = self.format_box.get()
        outdir = self.dir_var.get().strip()
        if not url:
            messagebox.showwarning('Missing URL', 'Please enter a valid video URL.')
            return
        if not os.path.isdir(outdir):
            messagebox.showwarning('Invalid Path', 'Please choose a valid folder.')
            return
        job_id = len(self.queue)
        job = {'id':job_id, 'url':url, 'fmt':fmt, 'outdir':outdir, 'status':'Queued', 'cancel':False}
        self.queue.append(job)
        self.tree.insert('', 'end', iid=job_id, values=(url, fmt, job['status'], '0%'))
        self.url_var.set('')

    def start_all(self):
        for job in self.queue:
            if job['status'] == 'Queued':
                job['status'] = 'Downloading'
                self.tree.set(job['id'], 'Status', 'Downloading')
                threading.Thread(target=self.download_task, args=(job,), daemon=True).start()

    def cancel_selected(self):
        for iid in self.tree.selection():
            job = self.queue[int(iid)]
            job['cancel'] = True
            job['status'] = 'Cancelled'
            self.tree.set(job['id'], 'Status', 'Cancelled')

    def download_task(self, job):
        def hook(d):
            if job['cancel']:
                raise Exception('Download cancelled')
            if d.get('status') == 'downloading':
                total = d.get('total_bytes') or d.get('total_bytes_estimate')
                downloaded = d.get('downloaded_bytes',0)
                if total:
                    pct = int(downloaded/total*100)
                    self.tree.set(job['id'],'Progress',f"{pct}%")

        opts = {'outtmpl':os.path.join(job['outdir'],'%(title)s.%(ext)s'),'progress_hooks':[hook]}
        if job['fmt'].startswith('Video'):
            opts['format']='bestvideo[ext=mp4]+bestaudio/best'
        else:
            opts['format']='bestaudio/best'
            opts['postprocessors']=[{'key':'FFmpegExtractAudio','preferredcodec':'mp3','preferredquality':'192'}]
        try:
            with YoutubeDL(opts) as ydl:
                ydl.download([job['url']])
            if not job['cancel']:
                job['status']='Finished'
                self.tree.set(job['id'],'Status','Finished')
        except Exception:
            job['status']='Cancelled' if job['cancel'] else 'Error'
            self.tree.set(job['id'],'Status',job['status'])

    def clear_completed(self):
        for job in list(self.queue):
            if job['status'] in ('Finished','Error','Cancelled'):
                self.tree.delete(job['id'])
                self.queue.remove(job)

    def auto_update(self):
        try:
            out = subprocess.run(['yt-dlp','-U'],capture_output=True,text=True)
            if 'Updating to version' in out.stdout:
                messagebox.showinfo('Update Available',out.stdout.strip())
        except Exception:
            pass


if __name__=='__main__':
    app=YTDL_GUI()
    app.mainloop()
