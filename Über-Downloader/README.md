# ⚡ The Whatever Downloader (TWD)

[![Version](https://img.shields.io/badge/version-1.0.0-blue.svg)](https://github.com/NLSolitaire/code-tele/releases)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-green.svg)](https://www.gnu.org/licenses/gpl-3.0)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011%20(x64)-lightgrey.svg)](https://microsoft.com/windows)
[![Ko-fi](https://img.shields.io/badge/Support-Ko--fi-ff5e5b.svg?logo=kofi&logoColor=white)](https://ko-fi.com/yddraiggoch)

**The Whatever Downloader** est un gestionnaire et téléchargeur multimédia moderne, ultra-rapide et ergonomique sous Windows, propulsé par [.NET 10 WPF](https://dotnet.microsoft.com/), [yt-dlp](https://github.com/yt-dlp/yt-dlp), [FFmpeg](https://ffmpeg.org/) et [Streamlink](https://streamlink.github.io/).

100% gratuit, open-source, sans aucune publicité ni télémétrie.

---

## 📥 Téléchargement & Installation

Pour installer et utiliser The Whatever Downloader :

1. Rendez-vous dans la section **[Releases](https://github.com/NLSolitaire/code-tele/releases)**.
2. Choisissez le format qui vous convient :
   - **📦 Installeur standard (`TheWhateverDownloader-v1.0.0-Setup-x64.exe`)** : Installe l'application localement dans votre profil utilisateur (sans droits administrateur requis), avec raccourcis menu Démarrer et Bureau.
   - **💼 Archive Portable (`TheWhateverDownloader-v1.0.0-win-x64-portable.zip`)** : Ne requiert aucune installation ni dépendance (.NET, yt-dlp et FFmpeg sont directement intégrés). Décompressez simplement l'archive où vous le souhaitez (clé USB, dossier personnel) et lancez `TheWhateverDownloader.exe`. Toutes les configurations restent isolées localement dans `data/`.
3. ⚠️ **Avertissement Windows SmartScreen ("Windows a protégé votre PC")** :  
   Comme l'application est un projet open-source récent non signé par un certificat commercial payant, Windows SmartScreen peut afficher un avertissement au premier lancement.  
   👉 Cliquez simplement sur **« Informations complémentaires »** puis sur **« Exécuter quand même »**.

---

## 🇫🇷 Français

### ✨ Fonctionnalités principales

- 🎬 **Téléchargements Vidéo & Audio complets** : Prise en charge de plus de 1000 plateformes (YouTube, Twitch, TikTok, etc.) avec sélection granulaire de la qualité, du codec (H.264, HEVC, AV1), des débits et des formats (MP4, MKV, WebM, MP3, M4A, FLAC, Opus).
- 🎵 **Indicateurs visuels audio dédiés** : Reconnaissance et identification immédiate des contenus audio avec icônes distinctives (🎵 pour les pistes audio simples, 🎶 pour les listes de lecture musicales).
- 🔴 **Enregistrement de Directs avec marge de sécurité disque** :
  - Choix du moteur d'enregistrement (**yt-dlp** ou **Streamlink**).
  - Mode au direct ou capture depuis le début (si supporté par la plateforme).
  - **Marge d'espace disque minimale configurable** (dès 1 Ko/KB, sans plafond) pour protéger votre stockage : l'enregistrement s'arrête proprement avant saturation. Alerte préventive dès le lancement si la marge est déjà atteinte.
- 📁 **Vérification d'intégrité du dossier de destination** : Détection et notification automatique au démarrage si le dossier de téléchargement a été déplacé, renommé ou supprimé manuellement dans l'Explorateur Windows.
- 📑 **Gestionnaire de Playlists & Modèles de nommage** : Prévisualisation interactive, sélection/désélection individuelle des pistes, et mémorisation fiable du format de nommage par défaut.
- 🌐 **Extraction de flux web (Sniffer)** : Capture dynamique des flux vidéo enfouis via automatisation de navigateur headless (Playwright / Chromium).
- 🌓 **Thème Sombre & Thème Clair haute lisibilité** : Console de journalisation (logs) avec contraste chromatique dynamique adapté au thème sélectionné.
- ▬ **Mode Barre Flottante & Raccourci Global** : Interface Always-on-Top compacte et escamotable, activable à tout instant via raccourci global (par défaut `Ctrl+Shift+O`).
- ⏱️ **Bandeau de confirmation éphémère** : Notifications discrètes s'auto-fermant après 3 secondes lors de l'enregistrement des préférences.
- 🛡️ **Vie privée absolue** : Zéro collecte de données, zéro télémétrie, exécution 100% locale.

### ☕ Soutenir le projet

The Whatever Downloader est un projet bénévole et indépendant. Si cette application vous est utile, vous pouvez m'offrir un café sur Ko-fi :  
👉 **[Faire un don sur Ko-fi](https://ko-fi.com/yddraiggoch)**

### ⚖️ Avis juridique et responsabilité

> **Important** : The Whatever Downloader est un outil technique conçu exclusivement pour un **usage personnel et légal** (copie privée, contenus sous licence libre, domaine public). Les auteurs déclinent toute responsabilité quant à l'utilisation non conforme aux lois sur le droit d'auteur ou aux conditions d'utilisation des plateformes tierces.

---

## 🇬🇧 English

### ✨ Key Features

- 🎬 **Comprehensive Video & Audio Downloads**: Support for 1,000+ platforms with granular control over resolution, video codecs (H.264, HEVC, AV1), bitrates, and audio formats (MP4, MKV, WebM, MP3, M4A, FLAC, Opus).
- 🎵 **Dedicated Audio Indicators**: Instant recognition of audio-only downloads with dedicated badges (🎵 for individual audio files, 🎶 for audio playlists).
- 🔴 **Live Stream Recording with Disk Space Margin**:
  - Dual recording engine (**yt-dlp** or **Streamlink**).
  - Live edge recording or rewind from start when available.
  - **Configurable minimum disk space safety margin** (from 1 KB up to any size) protecting your drive from running out of space, with early warning before recording starts.
- 📁 **Download Folder Validation**: Automatically verifies the destination directory on each startup, alerting you if it has been moved or removed in Windows Explorer.
- 📑 **Playlist Inspection & Persistent Naming Templates**: Preview playlist items, toggle individual items, and save custom naming schemes reliably.
- 🌐 **Browser Stream Sniffer**: Automated extraction of embedded video streams using headless browser technology.
- 🌓 **High-Contrast Dark & Light Log Viewer**: Dynamic color palette ensuring crystal-clear readability across both light and dark themes.
- ▬ **Floating Bar Mode & Global Shortcut**: Compact, always-on-top floating toolbar with configurable global hotkey (`Ctrl+Shift+O` by default).
- ⏱️ **Auto-Dismissing Notifications**: Success feedback banner quietly vanishes after 3 seconds.
- 🛡️ **Zero Telemetry**: No trackers, no analytics, no third-party data collection.

### ☕ Support

If you enjoy The Whatever Downloader and find it helpful, feel free to support the developer on Ko-fi:  
👉 **[Support on Ko-fi](https://ko-fi.com/yddraiggoch)**

### ⚖️ Legal Disclaimer

> The Whatever Downloader is intended for personal archiving and lawful use only. Users are solely responsible for compliance with applicable copyright legislation and platform terms of service.

---

## 🇪🇸 Español

### ✨ Características destacadas

- 🎬 **Descargas completas de Vídeo y Audio**: Compatible con más de 1000 plataformas con control detallado de formato, resolución, códecs y pistas de audio.
- 🎵 **Iconos especiales para contenido de audio**: Identificación visual inmediata con iconos específicos (🎵 para audio individual, 🎶 para listas de audio).
- 🔴 **Grabación en Vivo con margen de seguridad en disco**: Grabación en tiempo real (yt-dlp o Streamlink) con margen de espacio en disco configurable (a partir de 1 KB) para prevenir saturaciones.
- 📁 **Validación del directorio de descarga**: Comprobación automática al iniciar para advertir si la carpeta destino fue modificada o eliminada en el Explorador de Windows.
- 📑 **Gestor de Listas de Reproducción y plantillas de nombres**: Inspección selectiva y guardado fiable del modelo de nombrado de archivos.
- 🌓 **Registro (Logs) optimizado para temas Claro y Oscuro**: Legibilidad perfecta con paleta de colores dinámica.
- ▬ **Modo Barra Flotante y Atajo Global**: Barra compacta siempre visible con atajo configurable (`Ctrl+Shift+O`).
- 🛡️ **Privacidad total**: Sin anuncios, sin telemetría, ejecución 100% local.

👉 **[Apoyar en Ko-fi](https://ko-fi.com/yddraiggoch)**

---

## 🛠️ Compilation & Développement (.NET 10)

### Prérequis
- Windows 10 ou 11 (64-bit)
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Cloner et compiler
```powershell
git clone https://github.com/NLSolitaire/code-tele.git
cd code-tele\Über-Downloader
dotnet build src\UberDownloader.Wpf\UberDownloader.Wpf.csproj -c Release
```

---

## 📄 Licence & Liens

- **Dépôt officiel GitHub** : [https://github.com/NLSolitaire/code-tele](https://github.com/NLSolitaire/code-tele)
- **Licence** : Distribué sous licence **GNU General Public License v3.0 (GPL-3.0)**. Consultez le fichier [LICENSE](LICENSE).
- **Notices tierces** : Composants et bibliothèques tiers répertoriés dans [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
