import tkinter as tk
from tkinter import ttk, filedialog, messagebox
import tkinter.scrolledtext as scrolledtext
from threading import Thread
import yt_dlp
import os
import sys
import re
import time
import datetime
import calendar
import json
import socket
import urllib.request
import winsound
import subprocess

# --- Initialisation Playwright (optionnel / à la demande) ---
try:
    from playwright.sync_api import sync_playwright
    PLAYWRIGHT_AVAILABLE = True
except ImportError:
    PLAYWRIGHT_AVAILABLE = False

# --- Configuration & Constantes ---
APP_NAME = "U-DWN"
APP_DIR = os.path.dirname(os.path.abspath(__file__))
CONFIG_FILE = os.path.join(APP_DIR, "settings.json")
LOG_FILE = os.path.join(APP_DIR, "u_dwn.log")
DEFAULT_FFMPEG_PATH = r"C:\Users\HP\OneDrive\Documents\bin"
APP_PORT = 47653  # Port d'écoute pour l'instance unique
USER_AGENT_DEFAULT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36"

# Palette de Couleurs (Interface Claire & Épurée)
THEME = {
    "bg": "#f8fafc",
    "card_bg": "#ffffff",
    "border": "#cbd5e1",
    "text": "#0f172a",
    "subtext": "#64748b",
    "accent": "#2563eb",
    "accent_hover": "#1d4ed8",
    "success": "#16a34a",
    "danger": "#dc2626",
    "warning": "#d97706",
    "entry_bg": "#ffffff",
    "log_bg": "#0f172a",
    "log_fg": "#38bdf8",
    "bar_bg": "#f1f5f9",
}

# Constantes pour le sélecteur de date / calendrier
MONTH_NAMES_FR = [
    "", "Janvier", "Février", "Mars", "Avril", "Mai", "Juin",
    "Juillet", "Août", "Septembre", "Octobre", "Novembre", "Décembre"
]
DAY_NAMES_FR = ["Lu", "Ma", "Me", "Je", "Ve", "Sa", "Di"]

# --- Variables globales d'état ---
stop_download_flag = False
is_downloading = False
active_download_files = set()
current_ydl = None
download_thread = None
is_bar_mode = False
bar_notify_timer = None
saved_bar_x = None
saved_bar_y = None
selected_log_date = None

# --- Gestion des paramètres (settings.json) ---
def load_settings():
    defaults = {
        "download_folder": os.path.expanduser("~\\Downloads"),
        "quality": "Max HD/4K/8K",
        "browser_cookies": "aucun",
        "ffmpeg_path": DEFAULT_FFMPEG_PATH if os.path.exists(DEFAULT_FFMPEG_PATH) else "",
        "allow_playlist": True,
        "use_archive": False,
        "embed_subtitles": False,
        "embed_thumbnail": True,
        "play_sound": True,
        "speed_limit": "Illimité",
        "auto_sniff": True,
        "clean_incomplete_on_stop": False,
        "startup_mode": "Dernier mode utilisé",
        "last_mode": "full",
        "bar_x": None,
        "bar_y": None
    }
    if os.path.exists(CONFIG_FILE):
        try:
            with open(CONFIG_FILE, "r", encoding="utf-8") as f:
                saved = json.load(f)
                defaults.update(saved)
        except Exception:
            pass
    return defaults

def save_settings():
    global saved_bar_x, saved_bar_y
    if is_bar_mode:
        try:
            saved_bar_x = root.winfo_x()
            saved_bar_y = root.winfo_y()
        except Exception:
            pass

    settings = {
        "download_folder": path_var.get(),
        "quality": quality_var.get(),
        "browser_cookies": cookie_var.get(),
        "ffmpeg_path": ffmpeg_var.get(),
        "allow_playlist": is_playlist_var.get(),
        "use_archive": use_archive_var.get(),
        "embed_subtitles": subtitles_var.get(),
        "embed_thumbnail": thumbnail_var.get(),
        "play_sound": sound_var.get(),
        "speed_limit": speed_var.get(),
        "auto_sniff": auto_sniff_var.get(),
        "clean_incomplete_on_stop": clean_incomplete_var.get(),
        "startup_mode": startup_mode_var.get(),
        "last_mode": "bar" if is_bar_mode else "full",
        "bar_x": saved_bar_x,
        "bar_y": saved_bar_y
    }
    try:
        with open(CONFIG_FILE, "w", encoding="utf-8") as f:
            json.dump(settings, f, indent=4)
    except Exception:
        pass

# --- Vérification de la connexion Internet ---
def check_internet():
    try:
        urllib.request.urlopen('https://www.google.com', timeout=4)
        return True
    except OSError:
        return False

# --- Formatage de la taille des données (Ko, Mo, Go) ---
def format_bytes(bytes_val):
    if bytes_val is None or bytes_val <= 0:
        return "--"
    if bytes_val < 1024 * 1024:
        return f"{bytes_val / 1024:.1f} Ko"
    elif bytes_val < 1024 * 1024 * 1024:
        return f"{bytes_val / (1024 * 1024):.1f} Mo"
    else:
        return f"{bytes_val / (1024 * 1024 * 1024):.2f} Go"

# --- Popup Calendrier Personnalisé pour Filtrer les Logs ---
class MiniCalendarPopup(tk.Toplevel):
    def __init__(self, parent, target_widget, current_date_str=None, on_select=None):
        super().__init__(parent)
        self.withdraw()
        self.overrideredirect(True)
        self.configure(bg="#ffffff", highlightthickness=1, highlightbackground="#94a3b8")
        self.on_select = on_select
        self.target_widget = target_widget

        today = datetime.date.today()
        if current_date_str:
            try:
                dt = datetime.datetime.strptime(current_date_str, "%Y-%m-%d").date()
                self.year = dt.year
                self.month = dt.month
                self.selected_date = dt
            except Exception:
                self.year = today.year
                self.month = today.month
                self.selected_date = today
        else:
            self.year = today.year
            self.month = today.month
            self.selected_date = today

        self.container = tk.Frame(self, bg="#ffffff", padx=8, pady=8)
        self.container.pack(fill="both", expand=True)

        self._render_calendar()
        self._position_popup()
        self.deiconify()

        self.bind("<Escape>", lambda e: self.destroy())
        self.bind("<FocusOut>", self._on_focus_out)
        self.focus_force()

    def _on_focus_out(self, event=None):
        self.after(150, self._check_focus)

    def _check_focus(self):
        try:
            focused = self.focus_get()
            if focused is None or (focused != self and focused.winfo_toplevel() != self):
                self.destroy()
        except Exception:
            self.destroy()

    def _position_popup(self):
        self.update_idletasks()
        try:
            x = self.target_widget.winfo_rootx()
            y = self.target_widget.winfo_rooty() + self.target_widget.winfo_height() + 4
            sw = self.winfo_screenwidth()
            sh = self.winfo_screenheight()
            w = self.winfo_reqwidth()
            h = self.winfo_reqheight()
            if x + w > sw:
                x = sw - w - 10
            if y + h > sh:
                y = self.target_widget.winfo_rooty() - h - 4
            self.geometry(f"+{x}+{y}")
        except Exception:
            pass

    def _prev_month(self):
        if self.month == 1:
            self.month = 12
            self.year -= 1
        else:
            self.month -= 1
        self._render_calendar()

    def _next_month(self):
        if self.month == 12:
            self.month = 1
            self.year += 1
        else:
            self.month += 1
        self._render_calendar()

    def _render_calendar(self):
        for w in self.container.winfo_children():
            w.destroy()

        hdr = tk.Frame(self.container, bg="#ffffff")
        hdr.pack(fill="x", pady=(0, 6))

        btn_prev = tk.Button(hdr, text="◀", command=self._prev_month, font=("Segoe UI", 8, "bold"),
                             bg="#f1f5f9", fg="#0f172a", relief="flat", padx=6, pady=1, cursor="hand2")
        btn_prev.pack(side="left")

        month_label = tk.Label(hdr, text=f"{MONTH_NAMES_FR[self.month]} {self.year}",
                               font=("Segoe UI", 9, "bold"), bg="#ffffff", fg="#0f172a")
        month_label.pack(side="left", expand=True)

        btn_next = tk.Button(hdr, text="▶", command=self._next_month, font=("Segoe UI", 8, "bold"),
                             bg="#f1f5f9", fg="#0f172a", relief="flat", padx=6, pady=1, cursor="hand2")
        btn_next.pack(side="right")

        grid_frame = tk.Frame(self.container, bg="#ffffff")
        grid_frame.pack(fill="both", expand=True)

        for col, day_name in enumerate(DAY_NAMES_FR):
            lbl = tk.Label(grid_frame, text=day_name, font=("Segoe UI", 8, "bold"),
                           bg="#ffffff", fg="#64748b", width=3)
            lbl.grid(row=0, column=col, padx=1, pady=2)

        cal = calendar.monthcalendar(self.year, self.month)
        today = datetime.date.today()

        for r_idx, week in enumerate(cal, 1):
            for c_idx, day in enumerate(week):
                if day == 0:
                    lbl = tk.Label(grid_frame, text="", bg="#ffffff", width=3)
                    lbl.grid(row=r_idx, column=c_idx, padx=1, pady=1)
                else:
                    curr_d = datetime.date(self.year, self.month, day)
                    is_selected = (self.selected_date == curr_d)
                    is_today = (today == curr_d)

                    if is_selected:
                        bg_c = "#2563eb"
                        fg_c = "#ffffff"
                    elif is_today:
                        bg_c = "#dbeafe"
                        fg_c = "#1d4ed8"
                    else:
                        bg_c = "#f8fafc"
                        fg_c = "#0f172a"

                    btn = tk.Button(grid_frame, text=str(day), font=("Segoe UI", 8, "bold" if (is_selected or is_today) else "normal"),
                                    bg=bg_c, fg=fg_c, relief="flat", width=3, cursor="hand2",
                                    command=lambda d=day: self._select_day(d))
                    btn.grid(row=r_idx, column=c_idx, padx=1, pady=1)

        bottom_frame = tk.Frame(self.container, bg="#ffffff")
        bottom_frame.pack(fill="x", pady=(6, 0))

        btn_all = tk.Button(bottom_frame, text="📅 Tous", font=("Segoe UI", 8, "bold"),
                            bg="#f1f5f9", fg="#0f172a", relief="flat", padx=6, pady=2, cursor="hand2",
                            command=self._select_all)
        btn_all.pack(side="left", padx=(0, 2))

        btn_today = tk.Button(bottom_frame, text="Aujourd'hui", font=("Segoe UI", 8),
                              bg="#f1f5f9", fg="#2563eb", relief="flat", padx=6, pady=2, cursor="hand2",
                              command=self._select_today)
        btn_today.pack(side="left", padx=2)

        btn_close = tk.Button(bottom_frame, text="✕", font=("Segoe UI", 8),
                              bg="#fee2e2", fg="#dc2626", relief="flat", padx=6, pady=2, cursor="hand2",
                              command=self.destroy)
        btn_close.pack(side="right")

    def _select_day(self, day):
        chosen = f"{self.year:04d}-{self.month:02d}-{day:02d}"
        if self.on_select:
            self.on_select(chosen)
        self.destroy()

    def _select_today(self):
        today_str = datetime.date.today().strftime("%Y-%m-%d")
        if self.on_select:
            self.on_select(today_str)
        self.destroy()

    def _select_all(self):
        if self.on_select:
            self.on_select(None)
        self.destroy()

def open_calendar_popup(target_btn):
    """Ouvre le mini calendrier sous le bouton de filtre de logs."""
    MiniCalendarPopup(root, target_btn, current_date_str=selected_log_date, on_select=refresh_log_view)

# --- Journalisation / Logs Persistants & Affichage ---
def log_message(msg, level="INFO"):
    timestamp = time.strftime("%Y-%m-%d %H:%M:%S")
    current_date = timestamp.split()[0]
    formatted = f"[{timestamp}] [{level}] {msg}\n"
    
    # 1. Écriture persistante dans le fichier log
    try:
        with open(LOG_FILE, "a", encoding="utf-8") as f:
            f.write(formatted)
    except Exception:
        pass

    # 2. Mise à jour de la console UI
    def _write():
        try:
            # Si un filtre de date précis est actif et différent d'aujourd'hui, ne pas mélanger
            if selected_log_date and selected_log_date != current_date:
                return

            log_text.config(state="normal")
            
            # Ancien message repasse en couleur standard
            log_text.tag_remove("last_msg", "1.0", tk.END)
            log_text.tag_add("normal_log", "1.0", tk.END)

            # Nouveau message inséré en JAUNE vif
            log_text.insert(tk.END, formatted, "last_msg")
            log_text.see(tk.END)
            log_text.config(state="disabled")
            
            if selected_log_date and 'log_filter_info' in globals():
                cnt = int(log_text.index('end-1c').split('.')[0])
                log_filter_info.config(text=f"({cnt} ligne{'s' if cnt > 1 else ''})")
        except Exception:
            pass
    root.after(0, _write)

def refresh_log_view(target_date=None):
    """Filtre et affiche les logs selon la date sélectionnée dans le calendrier (ou tous les logs récents)."""
    global selected_log_date
    selected_log_date = target_date

    lines = []
    if os.path.exists(LOG_FILE):
        try:
            with open(LOG_FILE, "r", encoding="utf-8") as f:
                all_lines = f.readlines()
            if target_date:
                prefix = f"[{target_date}"
                lines = [l for l in all_lines if l.startswith(prefix)]
            else:
                lines = all_lines[-400:]
        except Exception:
            lines = []

    def _update():
        try:
            log_text.config(state="normal")
            log_text.delete("1.0", tk.END)

            if not lines:
                if target_date:
                    msg = f"[{target_date} 00:00:00] [INFO] Aucun log enregistré pour le {target_date}.\n"
                else:
                    msg = f"[{time.strftime('%Y-%m-%d %H:%M:%S')}] [INFO] Aucun journal disponible.\n"
                log_text.insert(tk.END, msg, "last_msg")
            else:
                for l in lines[:-1]:
                    log_text.insert(tk.END, l, "normal_log")
                # Le tout dernier message est affiché en JAUNE
                log_text.insert(tk.END, lines[-1], "last_msg")

            log_text.see(tk.END)
            log_text.config(state="disabled")

            if 'cal_btn' in globals():
                if target_date:
                    cal_btn.config(text=f"📅 {target_date} ▾", bg="#dbeafe", fg="#1d4ed8")
                    if 'log_filter_info' in globals():
                        log_filter_info.config(text=f"({len(lines)} entrée{'s' if len(lines) > 1 else ''})")
                else:
                    cal_btn.config(text="📅 Tous les logs ▾", bg="#f1f5f9", fg=THEME["text"])
                    if 'log_filter_info' in globals():
                        log_filter_info.config(text="")
        except Exception:
            pass
    root.after(0, _update)

def load_persistent_logs():
    """Charge l'historique récent des logs au démarrage."""
    refresh_log_view(None)

def clear_logs_action():
    """Efface la vue UI et vide le fichier journal."""
    try:
        with open(LOG_FILE, "w", encoding="utf-8") as f:
            f.write(f"[{time.strftime('%Y-%m-%d %H:%M:%S')}] [INFO] Journal réinitialisé.\n")
    except Exception:
        pass
    refresh_log_view(selected_log_date)

# --- Suivi UI Thread-Safe ---
def update_status(text, color=None):
    def _update():
        c = color if color else THEME["text"]
        status_label.config(text=text, fg=c)
        if is_bar_mode:
            bar_status_label.config(text=text[:32], fg=c)
    root.after(0, _update)

def update_progress(percent, text="", size_text=""):
    def _update():
        progress_var.set(percent)
        lbl = text if text else f"{percent:.1f}%"
        progress_label.config(text=lbl)
        bar_prog_label.config(text=f"{int(percent)}%")

        st = size_text if size_text else "📦 Taille : --"
        prog_size_label.config(text=st)

        # Taille abrégée pour le mode barre flottante
        short_size = size_text.replace("📦", "").strip() if size_text else ""
        if "/" in short_size:
            short_size = short_size.split("/")[-1].strip()
        bar_size_label.config(text=short_size if short_size != "--" else "")
    root.after(0, _update)

def set_fusion_mode(active, text=""):
    """Bascule la barre de progression en mode bleu pour la phase de fusion/finalisation FFmpeg."""
    def _update():
        if active:
            prog_bar.config(style="Fusion.Horizontal.TProgressbar")
            bar_prog_bar.config(style="Fusion.Horizontal.TProgressbar")
            progress_var.set(100)
            lbl = text if text else "🔄 Fusion vidéo + audio..."
            progress_label.config(text=lbl, foreground="#2563eb")
            bar_prog_label.config(text="Fusion...", fg="#2563eb")
            prog_size_label.config(text="🔄 Fusion FFmpeg")
            bar_size_label.config(text="Fusion")
            update_status("🔄 Fusion et finalisation des flux en cours (FFmpeg)...", "#2563eb")
        else:
            prog_bar.config(style="Download.Horizontal.TProgressbar")
            bar_prog_bar.config(style="Download.Horizontal.TProgressbar")
            progress_label.config(foreground=THEME["text"])
            bar_prog_label.config(fg=THEME["text"])
    root.after(0, _update)

def update_bar_widgets_state():
    """Synchronise l'affichage des composants de la barre flottante avec l'état de téléchargement."""
    if not is_bar_mode:
        return
    if is_downloading:
        bar_entry.pack_forget()
        bar_status_label.pack_forget()
        bar_progress_frame.pack(side="left", fill="x", expand=True, padx=4)
        bar_action_btn.config(text="⛔ Arrêter", bg=THEME["danger"], command=request_stop, state="normal")
    else:
        bar_progress_frame.pack_forget()
        bar_status_label.pack_forget()
        bar_entry.pack(side="left", fill="x", expand=True, padx=4)
        bar_action_btn.config(text="⬇ Télécharger", bg=THEME["success"], command=start_from_bar, state="normal")

def set_downloading_state(active):
    global is_downloading
    is_downloading = active
    def _update():
        state = "disabled" if active else "normal"
        start_btn.config(state=state)
        stop_btn.config(state="normal" if active else "disabled")
        links_text.config(state="disabled" if active else "normal")
        custom_title_entry.config(state="disabled" if active else "normal")
        if not active:
            set_fusion_mode(False)
        update_bar_widgets_state()
    root.after(0, _update)

# --- Notification Sonore ---
def play_notify_sound():
    if sound_var.get():
        try:
            winsound.MessageBeep(winsound.MB_ICONASTERISK)
        except Exception:
            pass

# --- Nettoyage des noms de fichiers Windows ---
def sanitize_filename(filename):
    """Supprime les caractères interdits pour les noms de fichiers Windows."""
    clean = re.sub(r'[\\/*?:"<>|]', "", filename).strip()
    return clean[:140]

# --- Extraction de flux média via Playwright (liens embed / SPA) ---
def sniff_video_stream(page_url, headless=True, timeout=35):
    if not PLAYWRIGHT_AVAILABLE:
        log_message("Playwright non disponible.", "WARN")
        return None, None

    captured_urls = []
    final_referer = page_url

    if not headless and is_bar_mode:
        root.after(0, lambda: root.attributes('-topmost', False))

    log_message(f"Démarrage analyseur réseau ({'invisible' if headless else 'fenêtre visible'})...")
    try:
        with sync_playwright() as p:
            browser = p.chromium.launch(headless=headless)
            context = browser.new_context(user_agent=USER_AGENT_DEFAULT)
            page = context.new_page()

            def handle_request(req):
                u = req.url
                if re.search(r'\.(m3u8|mpd|mp4)(\?|$)', u, re.IGNORECASE):
                    captured_urls.append(u)
                    log_message(f"Flux média détecté : {u[:90]}...", "SUCCESS")

            page.on("request", handle_request)

            try:
                page.goto(page_url, wait_until="domcontentloaded", timeout=timeout*1000)
                final_referer = page.url

                if headless:
                    for sel in ["button", ".play", "#play", "video", ".vjs-big-play-button"]:
                        try:
                            loc = page.locator(sel).first
                            if loc.is_visible(timeout=1000):
                                loc.click(timeout=1000)
                                break
                        except Exception:
                            pass

                start_t = time.time()
                while not captured_urls and (time.time() - start_t < timeout) and not stop_download_flag:
                    page.wait_for_timeout(500)

            except Exception as e:
                log_message(f"Note navigation : {str(e)[:80]}", "WARN")

            browser.close()

    except Exception as e:
        log_message(f"Erreur Playwright : {e}", "ERROR")
    finally:
        if not headless and is_bar_mode:
            root.after(0, lambda: root.attributes('-topmost', True))

    if captured_urls:
        masters = [u for u in captured_urls if "master" in u.lower() or u.endswith(".mp4")]
        best_url = masters[0] if masters else captured_urls[0]
        return best_url, final_referer

    return None, final_referer

# --- Formatage du temps restant (secondes, minutes, heures) ---
def format_eta(seconds):
    if seconds is None:
        return ""
    try:
        s = int(seconds)
        if s < 60:
            return f"{s}s"
        elif s < 3600:
            m = s // 60
            sec = s % 60
            return f"{m}min {sec}s" if sec > 0 else f"{m}min"
        else:
            h = s // 3600
            m = (s % 3600) // 60
            return f"{h}h {m}min" if m > 0 else f"{h}h"
    except Exception:
        return f"{seconds}s"

# --- Nettoyage des fichiers temporaires et incomplets ---
def cleanup_incomplete_files(folder, files_set):
    """Supprime les fichiers partiels, fragments et fichiers inachevés liés aux téléchargements annulés."""
    deleted_count = 0
    targets = set(files_set)

    # 1. Nettoyer les fichiers directement suivis et leurs déclinaisons (.part, .ytdl, etc.)
    for fpath in list(targets):
        if not fpath:
            continue
        candidates = [
            fpath,
            fpath + ".part",
            fpath + ".ytdl",
            fpath + ".temp",
            fpath + ".frag",
        ]
        for c in candidates:
            if os.path.exists(c):
                try:
                    os.remove(c)
                    deleted_count += 1
                    log_message(f"Fichier incomplet supprimé : {os.path.basename(c)}", "INFO")
                except Exception as e:
                    log_message(f"Impossible de supprimer {os.path.basename(c)} : {e}", "WARN")

        # Recherche de fragments intermédiaires (.f137, .f140...)
        dir_path = os.path.dirname(fpath)
        base_name = os.path.splitext(os.path.basename(fpath))[0]
        if os.path.exists(dir_path) and base_name:
            try:
                for item in os.listdir(dir_path):
                    if item.startswith(base_name[:25]) and (item.endswith(('.part', '.ytdl', '.temp')) or '.f' in item):
                        full_item = os.path.join(dir_path, item)
                        if os.path.exists(full_item):
                            try:
                                os.remove(full_item)
                                deleted_count += 1
                                log_message(f"Fragment temporaire supprimé : {item}", "INFO")
                            except Exception:
                                pass
            except Exception:
                pass

    # 2. Nettoyer les fichiers .part et .ytdl résiduels dans le dossier cible
    if folder and os.path.exists(folder):
        try:
            for item in os.listdir(folder):
                if item.endswith(('.part', '.ytdl', '.temp')):
                    full_item = os.path.join(folder, item)
                    try:
                        os.remove(full_item)
                        deleted_count += 1
                        log_message(f"Fichier temporaire orphelin supprimé : {item}", "INFO")
                    except Exception:
                        pass
        except Exception:
            pass

    return deleted_count

def manual_cleanup_action():
    """Déclenche un nettoyage manuel des fichiers .part du dossier cible."""
    folder = path_var.get().strip()
    if not folder or not os.path.exists(folder):
        messagebox.showerror("Erreur", "Veuillez sélectionner un dossier valide.")
        return
    deleted = cleanup_incomplete_files(folder, active_download_files)
    if deleted > 0:
        messagebox.showinfo("Nettoyage", f"Nettoyage réussi : {deleted} fichier(s) temporaire(s) (.part, .ytdl...) supprimé(s).")
    else:
        messagebox.showinfo("Nettoyage", "Aucun fichier temporaire (.part) trouvé dans le dossier.")

# --- Hook de post-traitement / fusion (FFmpeg) ---
def postprocessor_hook(d):
    if stop_download_flag:
        raise yt_dlp.utils.DownloadCancelled("Arrêt demandé par l'utilisateur.")
    status = d.get('status')
    pp_name = d.get('postprocessor', 'Traitement')
    if status == 'started':
        set_fusion_mode(True, f"🔄 Fusion / Finalisation ({pp_name})...")
        log_message(f"Démarrage de la fusion/finalisation : {pp_name}", "INFO")
    elif status == 'finished':
        log_message(f"Fin du traitement : {pp_name}", "INFO")

# --- Hook de progression yt-dlp ---
def progress_hook(d):
    global stop_download_flag, active_download_files
    if stop_download_flag:
        raise yt_dlp.utils.DownloadCancelled("Arrêt demandé par l'utilisateur.")

    # Enregistrement des fichiers en cours d'écriture
    fn = d.get('filename')
    tmp_fn = d.get('tmpfilename')
    if fn:
        active_download_files.add(os.path.abspath(fn))
    if tmp_fn:
        active_download_files.add(os.path.abspath(tmp_fn))

    status = d.get('status')
    if status == 'downloading':
        total = d.get('total_bytes') or d.get('total_bytes_estimate')
        downloaded = d.get('downloaded_bytes', 0)
        
        size_str = ""
        if total and total > 0:
            if downloaded > 0:
                size_str = f"{format_bytes(downloaded)} / {format_bytes(total)}"
            else:
                size_str = format_bytes(total)
        elif downloaded > 0:
            size_str = format_bytes(downloaded)
        else:
            size_str = "--"

        if not total and d.get('fragment_count'):
            f_count = d.get('fragment_count')
            f_idx = d.get('fragment_index', 0)
            pct = (f_idx / f_count) * 100
            eta = d.get('eta')
            eta_str = f" - {format_eta(eta)}" if eta else ""
            frag_size = format_bytes(downloaded) if downloaded else f"Frag {f_idx}/{f_count}"
            update_progress(pct, f"{pct:.1f}% ({f_idx}/{f_count}{eta_str})", size_text=f"📦 {frag_size}")
            return

        if total and total > 0:
            pct = (downloaded / total) * 100
            speed = d.get('speed')
            speed_str = f" - {speed / (1024*1024):.2f} Mo/s" if speed else ""
            eta = d.get('eta')
            eta_str = f" - {format_eta(eta)} restant" if eta else ""
            update_progress(pct, f"{pct:.1f}%{speed_str}{eta_str}", size_text=f"📦 {size_str}")
        elif downloaded > 0:
            speed = d.get('speed')
            speed_str = f" - {speed / (1024*1024):.2f} Mo/s" if speed else ""
            update_progress(0, f"Téléchargement...{speed_str}", size_text=f"📦 {size_str}")
    elif status == 'finished':
        set_fusion_mode(True, "🔄 Finalisation / Fusion...")

# --- Construction des options yt-dlp ---
def get_ydl_options(folder, quality_choice, custom_referer=None, custom_title=""):
    browser_cookie = cookie_var.get().strip().lower()
    allow_playlist = is_playlist_var.get()
    use_archive = use_archive_var.get()
    subtitles = subtitles_var.get()
    thumbnail = thumbnail_var.get()
    speed_limit = speed_var.get()

    ffmpeg_loc = ffmpeg_var.get().strip()
    if not ffmpeg_loc or not os.path.exists(ffmpeg_loc):
        ffmpeg_loc = DEFAULT_FFMPEG_PATH if os.path.exists(DEFAULT_FFMPEG_PATH) else None

    # Modèle de fichier : utilise le titre personnalisé si renseigné
    clean_custom_title = sanitize_filename(custom_title) if custom_title else ""
    if clean_custom_title:
        outtmpl = os.path.join(folder, f"{clean_custom_title}.%(ext)s")
    elif allow_playlist:
        outtmpl = os.path.join(folder, '%(playlist_title|Dossier)s', '%(playlist_index|0)s - %(title).120B [%(id)s].%(ext)s')
    else:
        outtmpl = os.path.join(folder, '%(title).150B [%(id)s].%(ext)s')

    opts = {
        'outtmpl': outtmpl,
        'ignoreerrors': True,
        'retries': 10,
        'fragment_retries': 10,
        'extractor_retries': 3,
        'continuedl': True,
        'concurrent_fragment_downloads': 4,
        'noplaylist': not allow_playlist,
        'windowsfilenames': True,
        'progress_hooks': [progress_hook],
        'postprocessor_hooks': [postprocessor_hook],
        'quiet': True,
        'noprogress': True,
    }

    if ffmpeg_loc:
        opts['ffmpeg_location'] = ffmpeg_loc

    if use_archive:
        opts['download_archive'] = os.path.join(folder, 'archive_telechargements.txt')

    if browser_cookie and browser_cookie != "aucun":
        try:
            opts['cookiesfrombrowser'] = (browser_cookie,)
        except Exception:
            pass

    headers = {'User-Agent': USER_AGENT_DEFAULT}
    if custom_referer:
        headers['Referer'] = custom_referer
    opts['http_headers'] = headers

    if speed_limit and speed_limit != "Illimité":
        match = re.search(r'(\d+)', speed_limit)
        if match:
            opts['ratelimit'] = int(match.group(1)) * 1024 * 1024

    if subtitles:
        opts['writesubtitles'] = True
        opts['writeautomaticsub'] = True
        opts['subtitleslangs'] = ['fr', 'en', 'all']

    postprocessors = []
    if thumbnail:
        opts['writethumbnail'] = True
        postprocessors.append({'key': 'EmbedThumbnail'})
    postprocessors.append({'key': 'FFmpegMetadata'})

    q = quality_choice.lower()
    if "max" in q or "4k" in q:
        opts.update({
            'format': 'bv*+ba/b',
            'merge_output_format': 'mp4',
            'format_sort': ['res', 'ext:mp4:m4a'],
        })
    elif "1080p" in q:
        opts.update({
            'format': 'bv*[height<=1080]+ba/b[height<=1080]/best',
            'merge_output_format': 'mp4',
        })
    elif "720p" in q:
        opts.update({
            'format': 'bv*[height<=720]+ba/b[height<=720]/best',
            'merge_output_format': 'mp4',
        })
    elif "480p" in q:
        opts.update({
            'format': 'bv*[height<=480]+ba/b[height<=480]/best',
            'merge_output_format': 'mp4',
        })
    elif "360p" in q:
        opts.update({
            'format': 'bv*[height<=360]+ba/b[height<=360]/best',
            'merge_output_format': 'mp4',
        })
    elif "mp3" in q:
        bitrate = "320" if "320k" in q else "192"
        opts.update({
            'format': 'bestaudio/best',
            'postprocessors': [{
                'key': 'FFmpegExtractAudio',
                'preferredcodec': 'mp3',
                'preferredquality': bitrate,
            }]
        })
        return opts
    elif "audio original" in q:
        opts.update({
            'format': 'bestaudio/best',
        })

    opts['postprocessors'] = postprocessors
    return opts

# --- Exécution de la file de téléchargement ---
def process_download_queue():
    global stop_download_flag, current_ydl

    raw_text = links_text.get("1.0", tk.END).strip()
    urls = [line.strip() for line in raw_text.splitlines() if line.strip() and not line.startswith("#")]
    
    if not urls:
        if not is_bar_mode:
            messagebox.showwarning("Attention", "Veuillez entrer au moins un lien.")
        else:
            show_bar_temporary_status("⚠️ Lien manquant", THEME["warning"])
        return

    folder = path_var.get().strip()
    if not folder:
        if not is_bar_mode:
            messagebox.showerror("Erreur", "Veuillez choisir un dossier de destination.")
        else:
            show_bar_temporary_status("❌ Dossier manquant", THEME["danger"])
        return

    if not check_internet():
        if not is_bar_mode:
            messagebox.showerror("Erreur", "Aucune connexion Internet détectée.")
        else:
            show_bar_temporary_status("❌ Pas de connexion", THEME["danger"])
        return

    custom_title = custom_title_var.get().strip()

    save_settings()
    stop_download_flag = False

    def worker():
        global current_ydl
        set_downloading_state(True)
        total_items = len(urls)
        success_count = 0
        error_count = 0

        log_message(f"--- Démarrage de la file U-DWN : {total_items} lien(s) ---")

        for idx, original_url in enumerate(urls, 1):
            if stop_download_flag:
                log_message("File interrompue par l'utilisateur.", "WARN")
                break

            set_fusion_mode(False)
            update_status(f"Téléchargement ({idx}/{total_items}) : {original_url[:35]}...", THEME["accent"])
            log_message(f"[{idx}/{total_items}] Analyse de : {original_url}")
            update_progress(0, "0%", size_text="📦 Analyse...")

            target_url = original_url
            custom_referer = None

            is_supported = False
            test_opts = {'quiet': True, 'simulate': True}
            try:
                with yt_dlp.YoutubeDL(test_opts) as ydl:
                    info = ydl.extract_info(original_url, download=False, process=False)
                    if info and info.get('ie_key') != 'Generic':
                        is_supported = True
            except Exception:
                is_supported = False

            if not is_supported and auto_sniff_var.get() and PLAYWRIGHT_AVAILABLE:
                update_status(f"🔍 Recherche du flux vidéo (Playwright)...", THEME["warning"])
                sniffed_url, ref = sniff_video_stream(original_url, headless=True)
                
                if not sniffed_url and not stop_download_flag:
                    update_status(f"👀 Cliquez sur PLAY dans la fenêtre...", THEME["warning"])
                    log_message("Ouverture fenêtre visible pour interaction utilisateur...")
                    sniffed_url, ref = sniff_video_stream(original_url, headless=False, timeout=50)

                if sniffed_url:
                    target_url = sniffed_url
                    custom_referer = ref
                    log_message(f"Flux extrait avec succès : {target_url[:80]}", "SUCCESS")
                else:
                    log_message("Impossible d'extraire automatiquement le flux vidéo.", "WARN")

            # Utiliser le titre personnalisé uniquement pour le premier fichier s'il y a plusieurs liens
            title_for_item = custom_title if (total_items == 1 or idx == 1) else ""
            opts = get_ydl_options(folder, quality_var.get(), custom_referer, custom_title=title_for_item)

            try:
                with yt_dlp.YoutubeDL(opts) as ydl:
                    current_ydl = ydl
                    code = ydl.download([target_url])
                if code == 0:
                    success_count += 1
                    log_message(f"[{idx}/{total_items}] Téléchargement terminé avec succès !", "SUCCESS")
                else:
                    error_count += 1
                    log_message(f"[{idx}/{total_items}] Avertissement ou échec de téléchargement.", "WARN")
            except yt_dlp.utils.DownloadCancelled:
                log_message("Téléchargement annulé.", "WARN")
                break
            except Exception as e:
                error_count += 1
                log_message(f"Erreur sur {original_url[:30]} : {e}", "ERROR")

        current_ydl = None
        set_downloading_state(False)

        if stop_download_flag:
            update_status("⛔ Téléchargement arrêté.", THEME["danger"])
            
            # Gestion de la suppression des fichiers incomplets
            files_to_clean = set(active_download_files)
            if clean_incomplete_var.get():
                # Suppression automatique configurée
                deleted = cleanup_incomplete_files(folder, files_to_clean)
                log_message(f"Nettoyage automatique post-arrêt : {deleted} fichier(s) supprimé(s).", "INFO")
                if is_bar_mode:
                    show_bar_temporary_status("⛔ Arrêté & Nettoyé", THEME["danger"])
            else:
                # Proposer l'option à l'utilisateur
                if not is_bar_mode:
                    def ask_cleanup_full():
                        if messagebox.askyesno("Supprimer les fichiers incomplets ?",
                                               "Le téléchargement a été interrompu.\n\nSouhaitez-vous supprimer les fichiers temporaires et incomplets (.part, fragments) créés ?"):
                            deleted = cleanup_incomplete_files(folder, files_to_clean)
                            log_message(f"Nettoyage validé : {deleted} fichier(s) supprimé(s).", "INFO")
                            update_status(f"⛔ Arrêté ({deleted} fichier(s) incomplet(s) supprimé(s))", THEME["danger"])
                    root.after(0, ask_cleanup_full)
                else:
                    def ask_cleanup_bar():
                        if messagebox.askyesno("Supprimer les fichiers incomplets ?",
                                               "Téléchargement arrêté.\n\nSupprimer les fichiers temporaires et partiels (.part) ?"):
                            deleted = cleanup_incomplete_files(folder, files_to_clean)
                            log_message(f"Nettoyage barre : {deleted} fichier(s) supprimé(s).", "INFO")
                            show_bar_temporary_status("🧹 Incomplets supprimés", THEME["warning"])
                        else:
                            show_bar_temporary_status("⛔ Arrêté", THEME["danger"])
                    root.after(0, ask_cleanup_bar)
        else:
            final_msg = f"Terminé ! Succès: {success_count} | Échecs: {error_count}"
            update_status(f"✅ {final_msg}", THEME["success"])
            update_progress(100, "100% - Prêt", size_text="📦 Terminé")
            play_notify_sound()

            # Nettoyer le champ titre si utilisé
            if custom_title:
                root.after(0, lambda: custom_title_var.set(""))

            if is_bar_mode:
                status_txt = "✅ Terminé !" if error_count == 0 else f"⚠️ Terminé ({success_count}/{total_items})"
                c = THEME["success"] if error_count == 0 else THEME["warning"]
                show_bar_temporary_status(status_txt, c)
            else:
                root.after(0, lambda: messagebox.showinfo("Bilan", final_msg))

    t = Thread(target=worker, daemon=True)
    t.start()

def request_stop():
    global stop_download_flag
    stop_download_flag = True
    update_status("Arrêt demandé...", THEME["danger"])

# --- Lancement depuis la barre flottante ---
def start_from_bar():
    txt = bar_entry.get().strip()
    if not txt:
        if not links_text.get("1.0", tk.END).strip():
            show_bar_temporary_status("⚠️ Collez un lien", THEME["warning"])
            return
    else:
        lines = [line.strip() for line in txt.split() if line.strip()]
        if lines:
            links_text.delete("1.0", tk.END)
            links_text.insert(tk.END, "\n".join(lines))
        bar_entry.delete(0, tk.END)

    process_download_queue()

def show_bar_temporary_status(text, color):
    global bar_notify_timer
    def _show():
        global bar_notify_timer
        if bar_notify_timer:
            root.after_cancel(bar_notify_timer)
        bar_entry.pack_forget()
        bar_progress_frame.pack_forget()
        bar_status_label.config(text=text, fg=color)
        bar_status_label.pack(side="left", fill="x", expand=True, padx=4)

        def _revert():
            bar_status_label.pack_forget()
            bar_entry.pack(side="left", fill="x", expand=True, padx=4)
            bar_entry.delete(0, tk.END)
            bar_entry.focus_set()

        bar_notify_timer = root.after(3000, _revert)
    root.after(0, _show)

# --- Mise à jour de yt-dlp ---
def update_ytdlp_tool():
    def _run():
        log_message("Vérification et mise à jour de yt-dlp...")
        update_status("Mise à jour de yt-dlp...", THEME["accent"])
        try:
            res = subprocess.run([sys.executable, "-m", "pip", "install", "-U", "yt-dlp"],
                                 capture_output=True, text=True, check=True)
            log_message(f"Mise à jour terminée : {res.stdout.splitlines()[-1]}", "SUCCESS")
            update_status("yt-dlp est à jour !", THEME["success"])
            if not is_bar_mode:
                messagebox.showinfo("Mise à jour", "yt-dlp a été mis à jour avec succès !")
        except Exception as e:
            log_message(f"Échec mise à jour : {e}", "ERROR")
            update_status("Erreur mise à jour yt-dlp", THEME["danger"])
            if not is_bar_mode:
                messagebox.showerror("Erreur", f"Erreur lors de la mise à jour : {e}")
    Thread(target=_run, daemon=True).start()

# --- Création du raccourci Bureau U-DWN ---
def create_shortcut_action():
    try:
        from make_shortcut import create_desktop_shortcut
        ok, msg = create_desktop_shortcut()
        if ok:
            messagebox.showinfo("Raccourci U-DWN", "Le raccourci U-DWN sans console a été placé sur votre Bureau !\nRaccourci clavier : CTRL+SHIFT+L")
            log_message(f"Raccourci Bureau U-DWN créé : {msg}", "SUCCESS")
        else:
            messagebox.showerror("Erreur", f"Impossible de créer le raccourci : {msg}")
    except Exception as e:
        messagebox.showerror("Erreur", f"Erreur : {e}")

# --- Gestion du Mode Barre Horizontale Déplaçable ---
def enter_bar_mode():
    global is_bar_mode, saved_bar_x, saved_bar_y
    is_bar_mode = True
    save_settings()

    content_frame.pack_forget()
    bar_frame.pack(fill="both", expand=True)

    root.title(APP_NAME)
    root.attributes('-topmost', True)

    sw = root.winfo_screenwidth()
    bx = saved_bar_x if saved_bar_x is not None and 0 <= saved_bar_x < sw - 100 else (sw - 560) // 2
    by = saved_bar_y if saved_bar_y is not None and 0 <= saved_bar_y < 800 else 40

    root.minsize(480, 48)
    root.maxsize(1920, 52)
    root.geometry(f"560x50+{bx}+{by}")
    root.resizable(True, False)

    # Synchronise l'affichage (barre de progression ou champ de saisie)
    update_bar_widgets_state()
    if not is_downloading:
        bar_entry.focus_set()

def exit_bar_mode():
    global is_bar_mode, saved_bar_x, saved_bar_y
    if not is_bar_mode:
        return
    try:
        saved_bar_x = root.winfo_x()
        saved_bar_y = root.winfo_y()
    except Exception:
        pass

    is_bar_mode = False
    save_settings()

    bar_frame.pack_forget()
    content_frame.pack(fill="both", expand=True)

    root.title(f"{APP_NAME} - Téléchargeur Médias Pro")
    root.attributes('-topmost', False)
    root.minsize(550, 540)
    root.maxsize(10000, 10000)
    # Prend immédiatement la taille minimale demandée
    root.geometry("550x540")
    root.resizable(True, True)

    # Synchronise l'état des boutons dans la fenêtre complète
    state = "disabled" if is_downloading else "normal"
    start_btn.config(state=state)
    stop_btn.config(state="normal" if is_downloading else "disabled")
    links_text.config(state="disabled" if is_downloading else "normal")
    custom_title_entry.config(state="disabled" if is_downloading else "normal")

def on_window_configure(event):
    global saved_bar_x, saved_bar_y
    if is_bar_mode:
        if root.state() == 'zoomed':
            root.state('normal')
            exit_bar_mode()
            return
        if root.state() == 'normal':
            saved_bar_x = root.winfo_x()
            saved_bar_y = root.winfo_y()

def on_window_close():
    global saved_bar_x, saved_bar_y
    if is_downloading:
        # Téléchargement en cours : demander confirmation
        res = messagebox.askyesnocancel(
            "Téléchargement en cours",
            "Un téléchargement est actuellement en cours !\n\n"
            "• [Oui] : Laisser continuer en arrière-plan (masquer la fenêtre)\n"
            "• [Non] : Annuler le téléchargement et quitter complètement\n"
            "• [Annuler] : Rester sur cette fenêtre"
        )
        if res is None:
            # Annulation : ne rien faire
            return
        elif res is True:
            # Masquer la fenêtre et continuer en tâche de fond
            if is_bar_mode:
                try:
                    saved_bar_x = root.winfo_x()
                    saved_bar_y = root.winfo_y()
                except Exception:
                    pass
            save_settings()
            root.withdraw()
            log_message("Application masquée : téléchargement actif en arrière-plan.", "INFO")
            return
        else:
            # Arrêter proprement et quitter
            request_stop()
            save_settings()
            root.destroy()
            return

    # Si aucun téléchargement n'est actif :
    if is_bar_mode:
        try:
            saved_bar_x = root.winfo_x()
            saved_bar_y = root.winfo_y()
        except Exception:
            pass
        save_settings()
        root.withdraw()
    else:
        save_settings()
        root.destroy()

def exit_app_completely():
    if is_downloading:
        if not messagebox.askyesno("Quitter", "Un téléchargement est en cours.\n\nVoulez-vous vraiment l'interrompre et quitter l'application ?"):
            return
        request_stop()
    save_settings()
    root.destroy()

# --- Import de fichier de liens texte ---
def import_links_file():
    path = filedialog.askopenfilename(filetypes=[("Fichiers texte", "*.txt"), ("Tous fichiers", "*.*")])
    if path:
        try:
            with open(path, "r", encoding="utf-8") as f:
                content = f.read()
            links_text.insert(tk.END, ("\n" if links_text.get("1.0", tk.END).strip() else "") + content)
            log_message(f"Liens importés depuis {os.path.basename(path)}")
        except Exception as e:
            messagebox.showerror("Erreur", f"Impossible de lire le fichier : {e}")

# --- Sélection de dossier ---
def choose_folder():
    f = filedialog.askdirectory(initialdir=path_var.get())
    if f:
        path_var.set(f)
        save_settings()

# --- Instance Unique (Socket Local Robuste) ---
instance_server_socket = None

def single_instance_server():
    global instance_server_socket
    instance_server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    instance_server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    try:
        instance_server_socket.bind(("127.0.0.1", APP_PORT))
        instance_server_socket.listen(5)
    except socket.error:
        # Tenter d'envoyer le signal SHOW à l'instance active
        try:
            client = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            client.settimeout(2.0)
            client.connect(("127.0.0.1", APP_PORT))
            client.send(b"SHOW")
            client.close()
            # Si le signal a été envoyé avec succès à une vraie instance, quitter ce doublon
            sys.exit(0)
        except Exception:
            # Aucune instance ne répond réellement, continuer le démarrage normal
            instance_server_socket = None
            return

    def listen():
        while True:
            try:
                conn, _ = instance_server_socket.accept()
                data = conn.recv(1024)
                if b"SHOW" in data:
                    root.after(0, bring_to_front)
                conn.close()
            except Exception:
                break
    Thread(target=listen, daemon=True).start()

def bring_to_front():
    root.deiconify()
    root.attributes('-topmost', True)
    root.lift()
    root.focus_force()
    if is_bar_mode:
        update_bar_widgets_state()
    else:
        root.after(200, lambda: root.attributes('-topmost', False))

# ==========================================
# Construction de l'Interface Graphique (Tkinter)
# ==========================================
single_instance_server()

settings = load_settings()
saved_bar_x = settings.get("bar_x")
saved_bar_y = settings.get("bar_y")

root = tk.Tk()
root.title(f"{APP_NAME} - Téléchargeur Médias Pro")
root.geometry("550x540")
root.minsize(550, 540)
root.configure(bg=THEME["bg"])

# Configuration des styles ttk pour la barre de progression (vert pour téléchargement, bleu pour fusion)
ttk_style = ttk.Style()
ttk_style.theme_use("clam")

ttk_style.configure("Download.Horizontal.TProgressbar",
                    troughcolor="#e2e8f0",
                    background="#16a34a",
                    darkcolor="#15803d",
                    lightcolor="#22c55e",
                    bordercolor="#cbd5e1")

ttk_style.configure("Fusion.Horizontal.TProgressbar",
                    troughcolor="#dbeafe",
                    background="#2563eb",
                    darkcolor="#1d4ed8",
                    lightcolor="#3b82f6",
                    bordercolor="#93c5fd")

# Variables Tkinter
path_var = tk.StringVar(value=settings.get("download_folder"))
quality_var = tk.StringVar(value=settings.get("quality"))
cookie_var = tk.StringVar(value=settings.get("browser_cookies"))
ffmpeg_var = tk.StringVar(value=settings.get("ffmpeg_path"))
is_playlist_var = tk.BooleanVar(value=settings.get("allow_playlist"))
use_archive_var = tk.BooleanVar(value=settings.get("use_archive"))
subtitles_var = tk.BooleanVar(value=settings.get("embed_subtitles"))
thumbnail_var = tk.BooleanVar(value=settings.get("embed_thumbnail"))
sound_var = tk.BooleanVar(value=settings.get("play_sound"))
speed_var = tk.StringVar(value=settings.get("speed_limit"))
auto_sniff_var = tk.BooleanVar(value=settings.get("auto_sniff"))
clean_incomplete_var = tk.BooleanVar(value=settings.get("clean_incomplete_on_stop", False))
startup_mode_var = tk.StringVar(value=settings.get("startup_mode", "Dernier mode utilisé"))
custom_title_var = tk.StringVar(value="")
progress_var = tk.DoubleVar(value=0)

# ---------------------------------------------
# MODE BARRE HORIZONTALE COMPACTE DÉPLAÇABLE
# ---------------------------------------------
bar_frame = tk.Frame(root, padx=6, pady=6, bg=THEME["bar_bg"])

# 1. Sélecteur de qualité compact
bar_quality_combo = ttk.Combobox(bar_frame, textvariable=quality_var, values=[
    "Max HD/4K/8K",
    "1080p Full HD",
    "720p HD",
    "Audio MP3 (192k)"
], state="readonly", width=14)
bar_quality_combo.pack(side="left", padx=(0, 4))

# 2. Zone centrale (Saisie OU Barre de progression)
bar_entry = tk.Entry(bar_frame, font=("Segoe UI", 9), relief="solid", bd=1, bg=THEME["entry_bg"], fg=THEME["text"])
bar_entry.pack(side="left", fill="x", expand=True, padx=4)
bar_entry.bind("<Return>", lambda e: start_from_bar())

bar_progress_frame = tk.Frame(bar_frame, bg=THEME["bar_bg"])
bar_size_label = tk.Label(bar_progress_frame, text="", font=("Segoe UI", 8, "bold"), bg=THEME["bar_bg"], fg=THEME["subtext"])
bar_size_label.pack(side="left", padx=(0, 4))
bar_prog_bar = ttk.Progressbar(bar_progress_frame, orient="horizontal", mode="determinate", variable=progress_var, style="Download.Horizontal.TProgressbar")
bar_prog_bar.pack(side="left", fill="x", expand=True, padx=(0, 4))
bar_prog_label = tk.Label(bar_progress_frame, text="0%", font=("Segoe UI", 8, "bold"), bg=THEME["bar_bg"], fg=THEME["text"])
bar_prog_label.pack(side="right")

bar_status_label = tk.Label(bar_frame, text="", font=("Segoe UI", 9, "bold"), bg=THEME["bar_bg"], fg=THEME["text"])

# 3. Bouton Télécharger / Arrêter
bar_action_btn = tk.Button(bar_frame, text="⬇ Télécharger", command=start_from_bar,
                           bg=THEME["success"], fg="white", activebackground="#15803d", activeforeground="white",
                           font=("Segoe UI", 9, "bold"), relief="flat", padx=10, pady=3, cursor="hand2")
bar_action_btn.pack(side="right", padx=(4, 0))

# ---------------------------------------------
# MODE FENÊTRE COMPLÈTE (Onglets, Réglages, Logs)
# ---------------------------------------------
content_frame = tk.Frame(root, padx=16, pady=12, bg=THEME["bg"])
content_frame.pack(fill="both", expand=True)

# En-tête : Titre & Boutons de Contrôle
header_frame = tk.Frame(content_frame, bg=THEME["bg"])
header_frame.pack(fill="x", pady=(0, 8))

title_lbl = tk.Label(header_frame, text=f"⚡ {APP_NAME} Pro", font=("Segoe UI", 14, "bold"), bg=THEME["bg"], fg=THEME["text"])
title_lbl.pack(side="left")

exit_btn = tk.Button(header_frame, text="❌ Quitter", command=exit_app_completely, font=("Segoe UI", 8), relief="groove")
exit_btn.pack(side="right", padx=3)

bar_mode_btn = tk.Button(header_frame, text="▬ Mode Barre Flottante", command=enter_bar_mode,
                         font=("Segoe UI", 8, "bold"), bg="#e2e8f0", relief="groove", cursor="hand2")
bar_mode_btn.pack(side="right", padx=4)

update_btn = tk.Button(header_frame, text="🔄 MàJ yt-dlp", command=update_ytdlp_tool, font=("Segoe UI", 8), relief="groove")
update_btn.pack(side="right", padx=3)

# Onglets
tab_control = ttk.Notebook(content_frame)
tab_main = ttk.Frame(tab_control, padding=8)
tab_options = ttk.Frame(tab_control, padding=8)
tab_logs = ttk.Frame(tab_control, padding=8)

tab_control.add(tab_main, text="📥 Téléchargements")
tab_control.add(tab_options, text="⚙️ Réglages & Avancé")
tab_control.add(tab_logs, text="📜 Journal & Logs")
tab_control.pack(fill="both", expand=True)

# --- ONGLET 1 : TÉLÉCHARGEMENT PRINCIPAL ---
lbl_links = ttk.Label(tab_main, text="Liens à télécharger (1 par ligne - vidéos, playlists, embeds bysedikamoum...) :", font=("Segoe UI", 9, "bold"))
lbl_links.pack(anchor="w", pady=(0, 2))

links_text = scrolledtext.ScrolledText(tab_main, height=5, font=("Consolas", 9), relief="solid", bd=1,
                                       bg=THEME["entry_bg"], fg=THEME["text"], insertbackground=THEME["text"])
links_text.pack(fill="x", pady=(0, 4))

links_btn_bar = ttk.Frame(tab_main)
links_btn_bar.pack(fill="x", pady=(0, 6))

ttk.Button(links_btn_bar, text="📁 Importer fichier .txt", command=import_links_file).pack(side="left", padx=(0, 4))
ttk.Button(links_btn_bar, text="🧹 Effacer les liens", command=lambda: links_text.delete("1.0", tk.END)).pack(side="left")

# Champ Titre Personnalisé (Optionnel)
title_row = ttk.Frame(tab_main)
title_row.pack(fill="x", pady=(0, 6))

ttk.Label(title_row, text="Titre personnalisé :", font=("Segoe UI", 9, "bold")).pack(side="left")
custom_title_entry = ttk.Entry(title_row, textvariable=custom_title_var)
custom_title_entry.pack(side="left", fill="x", expand=True, padx=6)
ttk.Label(title_row, text="(Optionnel, vide = nom d'origine)", font=("Segoe UI", 8), foreground=THEME["subtext"]).pack(side="right")

# Sélecteur Dossier & Qualité
folder_row = ttk.Frame(tab_main)
folder_row.pack(fill="x", pady=(0, 6))

ttk.Label(folder_row, text="Dossier :", font=("Segoe UI", 9, "bold")).pack(side="left")
folder_entry = ttk.Entry(folder_row, textvariable=path_var)
folder_entry.pack(side="left", fill="x", expand=True, padx=6)
ttk.Button(folder_row, text="Parcourir...", command=choose_folder).pack(side="right")

quality_row = ttk.Frame(tab_main)
quality_row.pack(fill="x", pady=(0, 8))

ttk.Label(quality_row, text="Qualité :", font=("Segoe UI", 9, "bold")).pack(side="left")
quality_combo = ttk.Combobox(quality_row, textvariable=quality_var, values=[
    "Max HD/4K/8K",
    "1080p Full HD",
    "720p HD",
    "480p",
    "360p",
    "Audio MP3 (192k)",
    "Audio MP3 Haute qualité (320k)",
    "Audio Original"
], state="readonly", width=24)
quality_combo.pack(side="left", padx=6)

sniff_check = ttk.Checkbutton(quality_row, text="Détecter flux cachés (Playwright)", variable=auto_sniff_var)
sniff_check.pack(side="left", padx=6)

prog_container = ttk.Frame(tab_main)
prog_container.pack(fill="x", pady=(6, 2))

prog_info_frame = ttk.Frame(prog_container)
prog_info_frame.pack(fill="x", pady=(0, 2))

prog_size_label = ttk.Label(prog_info_frame, text="📦 Taille : --", font=("Segoe UI", 8, "bold"), foreground=THEME["subtext"])
prog_size_label.pack(side="left")

progress_label = ttk.Label(prog_info_frame, text="Prêt", font=("Segoe UI", 8))
progress_label.pack(side="right")

prog_bar = ttk.Progressbar(prog_container, orient="horizontal", mode="determinate", variable=progress_var, style="Download.Horizontal.TProgressbar")
prog_bar.pack(fill="x", pady=(2, 6))

status_label = tk.Label(tab_main, text="Prêt à télécharger", font=("Segoe UI", 9, "bold"),
                        bg=THEME["bg"], fg=THEME["text"], wraplength=540)
status_label.pack(pady=(2, 6))

action_bar = ttk.Frame(tab_main)
action_bar.pack(pady=4)

start_btn = tk.Button(action_bar, text="▶ Lancer le téléchargement", command=process_download_queue,
                      bg=THEME["success"], fg="white", activebackground="#15803d", activeforeground="white",
                      font=("Segoe UI", 10, "bold"), relief="flat", padx=16, pady=6, cursor="hand2")
start_btn.pack(side="left", padx=6)

stop_btn = tk.Button(action_bar, text="⛔ Arrêter", command=request_stop,
                     bg=THEME["danger"], fg="white", activebackground="#b91c1c", activeforeground="white",
                     font=("Segoe UI", 10, "bold"), relief="flat", padx=16, pady=6, state="disabled", cursor="hand2")
stop_btn.pack(side="left", padx=6)

# --- ONGLET 2 : RÉGLAGES & OPTIONS AVANCÉES ---
opt_grid = ttk.LabelFrame(tab_options, text=" Options de téléchargement ", padding=10)
opt_grid.pack(fill="x", pady=6)

ttk.Checkbutton(opt_grid, text="Télécharger toute la playlist / chaîne", variable=is_playlist_var).grid(row=0, column=0, sticky="w", pady=3)
ttk.Checkbutton(opt_grid, text="Historique anti-doublon (archive.txt)", variable=use_archive_var).grid(row=0, column=1, sticky="w", padx=10, pady=3)

ttk.Checkbutton(opt_grid, text="Télécharger sous-titres (si dispo)", variable=subtitles_var).grid(row=1, column=0, sticky="w", pady=3)
ttk.Checkbutton(opt_grid, text="Intégrer miniature et métadonnées", variable=thumbnail_var).grid(row=1, column=1, sticky="w", padx=10, pady=3)

ttk.Checkbutton(opt_grid, text="Notification sonore à la fin", variable=sound_var).grid(row=2, column=0, sticky="w", pady=3)
ttk.Checkbutton(opt_grid, text="Supprimer automatiquement les fichiers incomplets à l'arrêt", variable=clean_incomplete_var).grid(row=2, column=1, sticky="w", padx=10, pady=3)

# Bouton de nettoyage manuel des résidus
ttk.Button(opt_grid, text="🧹 Nettoyer les fichiers .part du dossier", command=manual_cleanup_action).grid(row=3, column=0, columnspan=2, sticky="w", pady=(4, 6))

limits_row = ttk.Frame(opt_grid)
limits_row.grid(row=4, column=0, columnspan=2, sticky="w", pady=6)

ttk.Label(limits_row, text="Cookies navigateur :").pack(side="left")
cookie_combo = ttk.Combobox(limits_row, textvariable=cookie_var, values=["aucun", "chrome", "edge", "firefox", "brave", "opera", "vivaldi"], state="readonly", width=9)
cookie_combo.pack(side="left", padx=(4, 12))

ttk.Label(limits_row, text="Limite de vitesse :").pack(side="left")
speed_combo = ttk.Combobox(limits_row, textvariable=speed_var, values=["Illimité", "1 Mo/s", "2 Mo/s", "5 Mo/s", "10 Mo/s"], state="readonly", width=10)
speed_combo.pack(side="left", padx=(4, 0))

# Démarrage & Raccourci Système
sys_frame = ttk.LabelFrame(tab_options, text=" Raccourci Windows & Démarrage ", padding=10)
sys_frame.pack(fill="x", pady=6)

start_row = ttk.Frame(sys_frame)
start_row.pack(fill="x", pady=3)
ttk.Label(start_row, text="Mode à l'ouverture :").pack(side="left")
start_combo = ttk.Combobox(start_row, textvariable=startup_mode_var, values=[
    "Dernier mode utilisé",
    "Barre horizontale",
    "Fenêtre complète"
], state="readonly", width=22)
start_combo.pack(side="left", padx=6)

ffmpeg_row = ttk.Frame(sys_frame)
ffmpeg_row.pack(fill="x", pady=3)
ttk.Label(ffmpeg_row, text="Dossier FFmpeg :").pack(side="left")
ffmpeg_entry = ttk.Entry(ffmpeg_row, textvariable=ffmpeg_var)
ffmpeg_entry.pack(side="left", fill="x", expand=True, padx=6)

sc_row = ttk.Frame(sys_frame)
sc_row.pack(fill="x", pady=6)
ttk.Button(sc_row, text=f"🔗 Créer raccourci Bureau {APP_NAME} (CTRL+SHIFT+L)", command=create_shortcut_action).pack(side="left")

# --- ONGLET 3 : JOURNAL DES ERREURS & LOGS PERSISTANTS ---
log_toolbar = ttk.Frame(tab_logs)
log_toolbar.pack(fill="x", pady=(0, 4))

ttk.Button(log_toolbar, text="Effacer", command=clear_logs_action).pack(side="left", padx=(0, 4))

cal_btn = tk.Button(log_toolbar, text="📅 Tous les logs ▾", command=lambda: open_calendar_popup(cal_btn),
                    font=("Segoe UI", 8, "bold"), bg="#f1f5f9", fg=THEME["text"], relief="groove", padx=6, pady=2, cursor="hand2")
cal_btn.pack(side="left", padx=3)

ttk.Button(log_toolbar, text="Tous", command=lambda: refresh_log_view(None)).pack(side="left", padx=2)

log_filter_info = ttk.Label(log_toolbar, text="", font=("Segoe UI", 8), foreground=THEME["accent"])
log_filter_info.pack(side="left", padx=4)

ttk.Label(log_toolbar, text=f"Fichier : {os.path.basename(LOG_FILE)}", font=("Segoe UI", 8), foreground=THEME["subtext"]).pack(side="right")

log_text = scrolledtext.ScrolledText(tab_logs, state="disabled", font=("Consolas", 8), relief="solid", bd=1,
                                     bg=THEME["log_bg"], fg=THEME["log_fg"])
log_text.tag_config("last_msg", foreground="#facc15", font=("Consolas", 8, "bold"))
log_text.tag_config("normal_log", foreground=THEME["log_fg"])
log_text.pack(fill="both", expand=True)

# Lier les événements de fenêtre
root.bind("<Configure>", on_window_configure)
root.protocol("WM_DELETE_WINDOW", on_window_close)

# Charger les logs existants
load_persistent_logs()
log_message(f"{APP_NAME} démarré et prêt.")

# Choix du mode d'ouverture au démarrage
startup_choice = startup_mode_var.get()
last_m = settings.get("last_mode", "full")

if startup_choice == "Barre horizontale" or (startup_choice == "Dernier mode utilisé" and last_m == "bar"):
    enter_bar_mode()

if __name__ == "__main__":
    root.mainloop()
