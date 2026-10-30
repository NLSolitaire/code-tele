# Notes de Version — Über-Downloader v1.0.0 (Alpha)

## 📌 Présentation de la version Alpha
Cette première version publique autonome (Alpha) d'**Über-Downloader** est destinée aux tests sur différents environnements Windows (Windows 10 et Windows 11, 64-bit).

### 📦 Composants 100% Embarqués (Zéro Dépendance)
- **Runtime .NET 10** : Directement compilé en mode autonome (*self-contained*). Aucune installation préalable de .NET n'est requise sur le système.
- **Moteur multimédia (dossier `bin/`)** : Le package inclut désormais directement dans son sous-dossier `bin/` :
  - **`yt-dlp.exe`** (version officielle autonome complète)
  - **`ffmpeg.exe`** et **`ffprobe.exe`** (builds 64-bit statiques complets de yt-dlp FFmpeg-Builds)
  
Aucune commande supplémentaire, aucun script Python et aucun outil externe ne sont nécessaires pour télécharger des vidéos, playlists ou convertir l'audio.

---

## 🎯 Protocole de Test (Checklist pour les testeurs)

### 1. Premier lancement
- [ ] **Décompression du ZIP Portable** dans un dossier (ex: Bureau ou `C:\Test`) OU **Exécution de l'installeur**.
- [ ] **Écran Windows SmartScreen** : Vérifier que le message « Windows a protégé votre PC » apparaît, cliquer sur « Informations complémentaires » puis « Exécuter quand même » (normal pour un binaire non signé commercialement).
- [ ] Vérifier que l'application s'ouvre rapidement sans message d'erreur de DLL manquante.

### 2. Téléchargements vidéo & audio (yt-dlp + FFmpeg)
- [ ] Tester un lien YouTube standard (ex: vidéo 1080p).
- [ ] Tester une conversion uniquement Audio (MP3 ou AAC) pour vérifier le transcodage FFmpeg.
- [ ] Vérifier que le fichier final atterrit bien dans le dossier de destination et qu'il est lisible (VLC, Lecteur multimédia Windows).
- [ ] Vérifier que la barre de progression avance de façon fluide et affiche la vitesse et le temps restant.

### 3. Directs (Lives)
- [ ] Tester le lancement de l'enregistrement d'un live en cours (Twitch ou YouTube Live).
- [ ] L'enregistrement utilise directement le moteur intégré yt-dlp.
- [ ] Arrêter l'enregistrement et vérifier que le fichier généré n'est pas corrompu.

### 4. Playlists
- [ ] Coller le lien d'une playlist (3 à 5 vidéos).
- [ ] Vérifier l'ouverture de la fenêtre de préparation de la playlist avec la sélection des éléments.
- [ ] Lancer le téléchargement de la sélection.

### 5. Ergonomie & Barres
- [ ] Tester le basculement vers la **Barre Flottante** (mode mini compact).
- [ ] Tester le raccourci clavier global (`Ctrl+Shift+O` par défaut).
- [ ] Tester le changement de thème (Nuit / Jour / Système) et de langue (FR / EN / ES) dans les Paramètres.
- [ ] Cliquer sur le bouton **Ko-fi** pour s'assurer qu'il ouvre bien `https://ko-fi.com/yddraiggoch`.

### 6. Extraction Web Sniffer (Optionnelle / Expérimentale)
- [ ] Cette fonctionnalité est désactivée par défaut dans les Paramètres.
- [ ] Si activée sans environnement Python/Playwright sur le PC, vérifier qu'elle affiche un message propre et explicite indiquant que Python est nécessaire, sans faire planter l'application.

### 7. Journalisation & Retours
- [ ] En cas de bug ou d'erreur, ouvrir l'onglet Paramètres -> « Journal des événements » pour copier les logs ou cliquer sur « Ouvrir le dossier AppData ».

---

## 🔒 Intégrité des binaires de cette version
Les sommes de contrôle SHA-256 de chaque archive générée sont listées dans le fichier `SHA256SUMS.txt` inclus dans la distribution.
