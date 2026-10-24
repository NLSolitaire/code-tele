# Notes de Version — Über-Downloader v1.0.0 (Alpha)

## 📌 Présentation de la version Alpha
Cette première version publique autonome (Alpha) d'**Über-Downloader** est destinée aux tests sur différents environnements Windows (Windows 10 et Windows 11, 64-bit).

L'application est **100% autonome (Self-Contained)** : le runtime .NET n'a **pas** besoin d'être installé sur la machine de test, tout est embarqué.

---

## 🎯 Protocole de Test (Checklist)

Pour valider l'application sur différents PC et configurations, voici ce qu'il est recommandé de vérifier :

### 1. Premier lancement
- [ ] **Décompression du ZIP Portable** dans un dossier (ex: Bureau ou `C:\Test`) OU **Exécution de l'installeur**.
- [ ] **Écran Windows SmartScreen** : Vérifier que le message « Windows a protégé votre PC » apparaît, cliquer sur « Informations complémentaires » puis « Exécuter quand même ».
- [ ] Vérifier que l'application s'ouvre rapidement sans message d'erreur manquant de DLL.

### 2. Dépendances automatiques
- [ ] Au premier lancement ou au premier téléchargement, vérifier que l'outil télécharge/initialise bien `yt-dlp` et `ffmpeg` en tâche de fond dans son dossier de données (`%LOCALAPPDATA%\UberDownloader` ou le dossier `data/` en mode portable).
- [ ] Vérifier que le statut dans les Paramètres indique que les outils sont prêts.

### 3. Téléchargements vidéo & audio
- [ ] Tester un lien YouTube standard (ex: vidéo courte 1080p).
- [ ] Tester une conversion uniquement Audio (MP3 ou AAC).
- [ ] Vérifier que le fichier final atterrit bien dans le dossier de destination choisi et qu'il est lisible (VLC, Lecteur Windows Media).
- [ ] Vérifier que la barre de progression avance de façon fluide et affiche la vitesse et le temps restant.

### 4. Directs (Lives)
- [ ] Tester le lancement de l'enregistrement d'un live en cours (Twitch ou YouTube Live).
- [ ] Arrêter l'enregistrement et vérifier que le fichier généré n'est pas corrompu.

### 5. Playlists
- [ ] Coller le lien d'une playlist (3 à 5 vidéos).
- [ ] Vérifier l'ouverture de la fenêtre de préparation de la playlist avec la sélection des éléments.
- [ ] Lancer le téléchargement de la sélection.

### 6. Ergonomie & Barres
- [ ] Tester le basculement vers la **Barre Flottante** (mode mini always-on-top).
- [ ] Tester le raccourci clavier global (`Ctrl+Shift+O` par défaut).
- [ ] Tester le changement de thème (Nuit / Jour) et de langue (FR / EN / ES) dans les Paramètres.
- [ ] Cliquer sur le bouton **Ko-fi** pour s'assurer qu'il ouvre bien `https://ko-fi.com/yddraiggoch`.

### 7. Journalisation
- [ ] En cas de bug ou d'erreur, ouvrir l'onglet Paramètres -> « Journal des événements » pour copier les logs ou cliquer sur « Ouvrir le dossier AppData ».

---

## 🔒 Intégrité des binaires de cette version
Les sommes de contrôle SHA-256 de chaque archive générée sont listées dans le fichier `SHA256SUMS.txt` inclus dans la distribution.
