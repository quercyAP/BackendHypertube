## Plan 3 – Transcodage audio/vidéo + préparation MediaStream

Objectif global : respecter au mieux le sujet Hypertube tout en restant raisonnable pour un projet d’étude.

- **Côté sujet (obligatoire)** :
  - Lancer le torrent côté serveur si le fichier n’est pas encore téléchargé.
  - Démarrer le stream "dès que suffisamment de données" sont disponibles.
  - Si la vidéo n’est pas lisible nativement par le navigateur, la *convertir à la volée* vers un format acceptable.
  - Supporter au minimum les vidéos en `.mkv`.

- **Côté projet perso** :
  - Se concentrer sur Chrome pour la démo (Firefox peut avoir des comportements différents audio/seek).
  - Avoir un pipeline codec clair : cible de sortie = **H.264 + AAC en MP4**.
  - Préparer un passage progressif vers un player basé sur **MediaSource/MediaStream API**.

---

### 1. Décisions codec (référence pour toute la suite)

- **Format cible officiel pour le navigateur** :
  - Vidéo : `H.264` (AVC)
  - Audio : `AAC`
  - Conteneur : `MP4` (Content-Type `video/mp4`)

- **Règles de décision côté backend (à implémenter / consolider)** :
  - `H.264 + AAC` dans MP4/WebM/MOV → **Remux rapide** vers MP4 si besoin.
  - `H.264 + {MP3, AC3, DTS, ...}` → **Transcodage audio vers AAC** (vidéo copiée) → MP4.
  - `HEVC` ou autres codecs vidéo exotiques → **Incompatible** (cas de test seulement, pas support officiel).
  - `Xvid/MPEG4 ASP`, conteneurs très exotiques → **Incompatible**.

Ces règles doivent être reflétées dans :

- `VideoCodecDetector` (détection + `IsWebCompatible`).
- `TorrentDownloadService.StartConversionIfNeededAsync` (logique de remux/transcodage + statuts).

#### 1.1. Matrice finale (résumé exécutable)

| Vidéo / Audio / Conteneur                           | Décision backend                        | Statuts côté torrent                |
|-----------------------------------------------------|-----------------------------------------|-------------------------------------|
| `H.264 + AAC` dans `mp4/webm/mov`                   | Remux rapide MP4                        | `Remuxing` → `Seeding`              |
| `H.264 + audio != AAC` (`mp3`, `ac3`, `dts`, ...)   | Transcodage **audio seul** vers AAC MP4 | `TranscodingAudio` → `Seeding`      |
| `H.264 + AAC` mais conteneur exotique               | Remux / copie vers MP4 si possible      | idem remux                          |
| `HEVC + AAC`                                        | Autorisé uniquement comme cas de test   | `CodecUnknown` (+ warning explicite) |
| `H.264 + MP3` non remuxable en l’état              | Transcodage audio vers AAC (implémenté) | `TranscodingAudio`                  |
| `Xvid/MPEG4 ASP`, codecs vidéo exotiques           | Non supporté                            | `Incompatible`                      |
| Aucun fichier avec extension vidéo sur disque      | Non supporté                            | `CodecUnknown` (torrent "archive") |
| `.torrent` invalide (clé `info` manquante/cassée)  | Non supporté                            | HTTP 400 côté API                   |

---

### 2. Étape 1 – Mode "fichier statique optimisé" (aujourd’hui)

But : stabiliser un mode simple et fiable pour la soutenance, en se reposant sur des fichiers finalisés côté serveur.

- **T1.1 – Consolider la matrice de décision codecs**
  - Documenter en code (log + commentaires courts) ce qui se passe pour :
    - `H.264 + AAC` (OK direct / remux MP4).
    - `H.264 + MP3` (Cas actuel = `CodecUnknown`, futur = transcode audio).
    - `H.264 + AC3/DTS`.
    - HEVC, Xvid, etc.

- **T1.2 – Implémenter un transcodage audio après téléchargement complet**
  - Dans `TorrentDownloadService.StartConversionIfNeededAsync` :
    - Si `Video == h264` et `Audio != aac` :
      - créer un chemin de sortie MP4 (`*_audioaac.mp4`).
      - statut = `TranscodingAudio`, `IsConverting = true`.
      - lancer `ffmpeg` : copie vidéo (`-c:v copy`), audio → AAC (`-c:a aac -b:a 160k`).
      - à la fin : `ConvertedFilePath = output`, `Status = Seeding`, `IsConverting = false`.
  - Gestion des erreurs :
    - log clair + `Status = Error` + `ErrorMessage` lisible côté front.

- **T1.3 – Adapter le front minimal**
  - Bouton `Play` désactivé si :
    - `IsConverting = true` ou `Status = TranscodingAudio`.
  - Afficher un message explicite :
    - "Conversion audio en cours (H.264 → AAC)...".
  - Une fois le statut passé à `Seeding`, autoriser la lecture du fichier MP4 converti.

- **T1.4 – Scénarios de démo (Chrome)**
  - Cas A : torrent déjà en `H.264 + AAC` →
    - téléchargement partiel → lecture possible pendant le DL (comme aujourd’hui), remux rapide si besoin.
  - Cas B : torrent en `H.264 + MP3` →
    - téléchargement complet → transcodage audio → lecture propre (image + son) depuis le MP4 final.

Remarque : cette étape n’implique **pas encore** de MediaSource/MSE, on reste sur un `<video src="/api/torrents/stream/...">` classique.

---

### 3. Étape 2 – Préparation MediaSource / MediaStream (bonus structuré)

Objectif : préparer un pipeline réaliste pour le bonus "Stream via MediaStream API" sans tout refaire.

- **T2.1 – Choisir la stratégie MediaSource de base**
  - Décider d’un format de segments :
    - soit **MP4 fragmenté** (ISO BMFF) via `ffmpeg -f mp4 -movflags frag_keyframe+empty_moov`.
    - soit **WebM** (VP9/Opus) *pour un test*, mais complexité plus grande.
  - Pour rester simple avec ce qu’on a déjà : préférer H.264+AAC/MP4.

- **T2.2 – Prototype MSE sur un fichier local déjà converti**
  - Sans torrent dans un premier temps :
    - backend : endpoint `/api/dev/segments/{movieId}` qui renvoie des chunks (pseudo-segmentation d’un MP4 déjà prêt).
    - front : nouvelle page de test (ou section) qui :
      - crée un `MediaSource` + `SourceBuffer('video/mp4; codecs="avc1.42E01E, mp4a.40.2"')`.
      - fetch les segments un par un et les pousse dans le buffer.
      - vérifie pause/seek dans ce contexte.

- **T2.3 – Lien avec les torrents (design, pas forcément tout implémenter)**
  - Documenter dans le code / README comment on pourrait brancher :
    - torrent downloader → fichier partiel → ffmpeg en pipe → segments → MediaSource.
  - Laisser ce point comme "bonus++" clairement expliqué mais pas forcément entièrement codé (selon le temps).

---

### 4. Validation et préparation soutenance

- **T3.1 – Script de démo**
  - **Jeu de torrents de référence (test.txt)** :
    - `Pirates of Silicon Valley` → H.264 + MP3 (GOOD, transcodage audio → `_audioaac.mp4`).
    - `Amélie` → H.264 + AAC (GOOD, remux rapide si besoin).
    - `La Haine` → single file sans vraie extension vidéo (NOOP, probablement archive/rar).
    - `WarGames` → `.torrent` mal formé (`info` invalide) → rejet API.

  - **Scénario démo Cas A – H.264 + AAC (Amélie)** :
    - `curl -X POST /api/torrents/download` avec l’URL torrent de **Amélie**.
    - Sur `/api/torrents` :
      - statut `Downloading` puis `Seeding`.
      - `FileFormat` = `mkv` ou `mp4`, `CanStreamNow = true`.
    - Ouvrir le mini player (`index.html`) :
      - `Load torrents` → voir le torrent `Amélie`.
      - Bouton `Play` actif dès que `isReadyForStreaming` > 5 %.
      - Vidéo lue directement, sans étape `TranscodingAudio` (remux éventuel côté backend).

  - **Scénario démo Cas B – H.264 + MP3 (Pirates of Silicon Valley)** :
    - `curl -X POST /api/torrents/download` avec l’URL torrent de **Pirates of Silicon Valley**.
    - Attendre la fin du téléchargement (progression 100 %).
    - Logs backend :
      - `Detected codecs - Video: h264, Audio: mp3, ... WebCompatible: False`.
      - `Starting audio-only transcode to AAC (H.264 video copy)...`.
      - `Audio-only transcode completed: ..._audioaac.mp4`.
    - Sur `/api/torrents` :
      - statut `TranscodingAudio` pendant la conversion.
      - `IsConverting = true`, `ConversionProgress` qui monte.
      - puis `Status = Seeding`, `FilePath` pointant vers `*_audioaac.mp4`.
    - Dans le mini player :
      - pendant `TranscodingAudio` → bouton `Play` désactivé, message :
        - "Conversion audio en cours (H.264 → AAC)...".
      - une fois `Seeding` → `Play` actif, lecture fluide (image + son) sur Chrome/Firefox.

  - **Scénario démo Cas C – torrents non supportés** :
    - `La Haine` :
      - `.torrent` parsé correctement, single file annoncé.
      - fichier final sur disque = `data` sans extension.
      - `StartConversionIfNeededAsync` ne trouve aucun `.mp4/.mkv/...` valide →
        - `Status = CodecUnknown`,
        - `ErrorMessage = "Unable to locate a valid video file in this torrent (no .mp4/.mkv/etc. found). Archives or extension-less data are not supported."`.
      - Côté mini player : badge `CodecUnknown`, texte expliquant que le torrent ne respecte pas les prérequis.

    - `WarGames` :
      - `curl -X POST /api/torrents/download` avec l’URL itorrents actuelle →
        - BencodeNET lève une erreur sur la section `info`.
      - `StartDownloadAsync` capture l’exception et jette une `ArgumentException` →
        - le contrôleur retourne HTTP **400** avec le message :
          - `"The downloaded file is not a valid .torrent (invalid or missing 'info' section)."`.
      - Permet de montrer que l’API distingue clairement un `.torrent` invalide d’un problème réseau.

- **T3.2 – Doc rapide**
  - Ajouter quelques lignes dans `README` ou `plan3.md` expliquant :
  - les formats réellement supportés :
    - cible officielle = H.264 + AAC en MP4,
    - `H.264 + MP3` supporté via transcodage audio,
    - HEVC, Xvid, archives, torrents multi-films et `.torrent` invalides considérés comme non supportés.
  - ce qui se passe quand un torrent ne respecte pas ces formats :
    - soit rejet précoce au niveau `.torrent` (`Status = Incompatible`),
    - soit `CodecUnknown` avec message explicite quand le fichier final n’est pas un vrai conteneur vidéo.
  - pourquoi Chrome est la cible principale de la démo :
    - compat vidéo/audio bien maîtrisée en H.264+AAC,
    - comportement plus prévisible sur le `<video>` natif pour la soutenance.
