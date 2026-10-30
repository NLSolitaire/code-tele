# ⚡ Über-Downloader

[![Version](https://img.shields.io/badge/version-1.0.0-blue.svg)](https://github.com/NLSolitaire/code-tele/releases)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-green.svg)](https://www.gnu.org/licenses/gpl-3.0)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011%20(x64)-lightgrey.svg)](https://microsoft.com/windows)
[![Ko-fi](https://img.shields.io/badge/Support-Ko--fi-ff5e5b.svg?logo=kofi&logoColor=white)](https://ko-fi.com/yddraiggoch)

**Über-Downloader** est un gestionnaire et téléchargeur multimédia moderne, ultra-rapide et ergonomique sous Windows, propulsé par [.NET 10 WPF](https://dotnet.microsoft.com/), [yt-dlp](https://github.com/yt-dlp/yt-dlp), [FFmpeg](https://ffmpeg.org/) et [Streamlink](https://streamlink.github.io/).

100% gratuit, open-source, sans aucune publicité ni télémétrie.

---

## 📥 Téléchargement & Installation (Alpha Test)

Pour installer et tester Über-Downloader sur votre PC :

1. Rendez-vous dans la section **[Releases](https://github.com/NLSolitaire/code-tele/releases)**.
2. Choisissez le format qui vous convient :
   - **📦 Installeur standard (`UberDownloader-v1.0.0-Setup-x64.exe`)** : Installe l'application localement dans votre profil utilisateur (sans nécessiter de droits administrateur), avec raccourcis menu Démarrer et Bureau.
   - **💼 Archive Portable (`UberDownloader-v1.0.0-win-x64-portable.zip`)** : Ne nécessite aucune installation ni dépendance (.NET, yt-dlp et FFmpeg sont directement intégrés). Décompressez simplement le dossier où vous le souhaitez (clé USB, dossier personnel) et lancez `UberDownloader.exe`. Toutes les données et configurations restent isolées dans le sous-dossier local `data/`.
3. ⚠️ **Avertissement Windows SmartScreen ("Windows a protégé votre PC")** :
   Comme l'application est un nouveau projet open-source non signé avec un certificat payant de grande autorité, Windows SmartScreen affichera un message bleu au premier lancement.  
   👉 Cliquez simplement sur **« Informations complémentaires »** puis sur le bouton **« Exécuter quand même »**.

---

## 🇫🇷 Français

### ✨ Fonctionnalités
- 🎬 **Téléchargements Vidéo & Audio** : Prise en charge de plus de 1000 plateformes (YouTube, Twitch, TikTok, etc.) avec sélection fine de la résolution, des codecs, des débits et des pistes audio/sous-titres.
- 🔴 **Enregistrement de Directs (Lives)** : Moteur d'enregistrement temps-réel adaptatif avec choix du moteur (yt-dlp ou Streamlink) et mode de capture (au direct ou depuis le début si supporté).
- 📑 **Gestionnaire de Playlists** : Inspection préalable, sélection des éléments individuels et téléchargement séquencé.
- 🌐 **Extraction de flux web (Sniffer)** : Capture dynamique des flux vidéo enfouis via automatisation de navigateur.
- ▬ **Mode Barre Flottante & Raccourci Global** : Interface Always-on-Top rétractable avec raccourci clavier global personnalisable (par défaut `Ctrl+Shift+O`).
- 🛡️ **Vie privée garantie** : Zéro collecte de données, zéro pistage, 100% local.

### ☕ Soutenir le projet
Über-Downloader est un projet bénévole et indépendant. Si cette application vous est utile, vous pouvez m'offrir un café pour soutenir son développement continu :  
👉 **[Faire un don sur Ko-fi](https://ko-fi.com/yddraiggoch)**

### ⚖️ Avis juridique et responsabilité
> **Important** : Über-Downloader est un utilitaire technique conçu exclusivement pour un **usage personnel et légal** (sauvegarde privée, contenus sous licence libre, domaine public). Les auteurs déclinent toute responsabilité quant à l'utilisation non conforme aux lois sur le droit d'auteur ou aux conditions générales des plateformes tierces.

---

## 🇬🇧 English

### ✨ Features
- 🎬 **Video & Audio Downloads**: Support for over 1,000 streaming platforms with granular quality, codec, audio language and subtitle options.
- 🔴 **Live Stream Recording**: Real-time recording powered by yt-dlp and Streamlink with customizable duration and restart resilience.
- 📑 **Playlist Manager**: Interactive preview and selective batch downloading.
- 🌐 **Browser Stream Sniffer**: Extraction of non-standard video streams using headless browser automation.
- ▬ **Floating Bar Mode & Global Hotkey**: Sleek compact always-on-top bar with custom global shortcut activation (`Ctrl+Shift+O` by default).
- 🛡️ **Privacy by Design**: No telemetry, no ads, no trackers. Completely private.

### ☕ Support
If you enjoy Über-Downloader and find it helpful, feel free to support the developer on Ko-fi:  
👉 **[Support on Ko-fi](https://ko-fi.com/yddraiggoch)**

### ⚖️ Legal Disclaimer
> Über-Downloader is intended for personal archiving and legal use only. Users are solely responsible for ensuring compliance with intellectual property laws and third-party platform terms of service.

---

## 🇪🇸 Español

### ✨ Características
- 🎬 **Descargas de Video y Audio**: Compatible con más de 1000 plataformas web con control detallado de formato, resolución y pistas.
- 🔴 **Grabación de Transmisiones en Vivo**: Grabador en vivo avanzado con yt-dlp y Streamlink.
- 📑 **Gestor de Listas de Reproducción**: Vista previa interactiva y selección granular de pistas.
- ▬ **Barra Flotante y Atajo Global**: Modo compacto flotante y atajo de teclado global configurable.
- 🛡️ **Privacidad Absoluta**: Sin telemetría ni anuncios. 100% local.

👉 **[Apoyar en Ko-fi](https://ko-fi.com/yddraiggoch)**

---

## 🛠️ Compilation & Développement (.NET 10)

### Prérequis
- Windows 10 ou 11 (64-bit)
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Cloner et compiler
```powershell
git clone https://github.com/NLSolitaire/code-tele.git
cd code-tele
dotnet build Über-Downloader\UberDownloader.slnx -c Release
```

---

## 📄 Licence

Distribué sous licence **GNU General Public License v3.0 (GPL-3.0)**. Consultez le fichier [LICENSE](LICENSE) pour plus de détails.  
Les notices et licences des composants tiers sont répertoriées dans [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
