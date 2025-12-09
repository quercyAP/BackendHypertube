## 1. Cible fonctionnelle

Pour un MKV/MP4 avec plusieurs pistes :

- **Backend** :
  - détecter les pistes de sous‑titres (langue, type, titre),
  - les extraire en fichiers WebVTT,
  - les exposer via des URLs stables.

- **Frontend (player MSE)** :
  - appeler une API `/api/mse/subtitles/{torrentId}`,
  - afficher une liste de pistes (FR, EN, “Forced”, etc.),
  - activer/désactiver une piste sur le `<video>` sans recharger la vidéo.

---

## 2. Flux backend proposé

En t’appuyant sur ce que tu as déjà (ffprobe + post‑traitement) :

1. **Détection des sous‑titres internes**

   Lors de [StartConversionIfNeededAsync](cci:1://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/Infrastructure/Hypertube.Infrastructure/Services/TorrentDownloadService.cs:425:4-583:5) (là où tu fais déjà `DetectCodecsAsync`) :

   - Tu ajoutes dans `IVideoCodecDetector` la détection des pistes `subtitle` :
     - index,
     - langue (`fr`, `en`, `jpn`…),
     - titre éventuel (`Forced`, `Signs & Songs`, etc.),
     - type codec (`subrip`, `ass`, `pgs`, etc.).
   - Tu retournes une petite liste `SubtitleTrackInfo` dans ton `codecInfo`.

2. **Décision : extraire ou pas**

   - Si `codecInfo.Subtitles.Count == 0` → rien à faire.
   - Sinon, pour chaque piste intéressante (par ex. rôle “subtitles textuels”, pas les PGS image si tu ne veux pas t’embêter) :
     - tu planifies une extraction dans le même pipeline que ton remux.

3. **Extraction en `.vtt`**

   Plusieurs options, le plus simple pour commencer :

   - Tu définis un dossier subtitles à côté du fichier vidéo final, par ex. :

     `wwwroot/subtitles/{torrentIdN}/sub_{index}_{lang}.vtt`

   - Tu appelles `ffmpeg` pour chaque piste :

     - `-map 0:s:{i}` pour choisir la piste,
     - soit conversion directe en WebVTT (`-f webvtt`),
     - soit d’abord extraction en `.srt` puis conversion en `.vtt`.

   - Tu logs bien ce que tu fais : `"[Subs] Extracted subtitle track 2 (fr, Forced) -> /subtitles/.../sub_2_fr_forced.vtt"`.

4. **API pour lister les sous‑titres**

   - Nouveau endpoint, genre :

     `GET /api/mse/subtitles/{torrentId}`

   - Retour JSON du type :

     ```json
     [
       { "id": "sub_0", "lang": "jpn", "label": "Japonais", "url": "/subtitles/{id}/..." },
       { "id": "sub_1", "lang": "en",  "label": "Anglais",  "url": "/subtitles/{id}/..." },
       { "id": "sub_2", "lang": "fr",  "label": "Français", "url": "/subtitles/{id}/..." }
     ]
     ```

   - Ce endpoint lit simplement la “métadonnée sous‑titre” que tu as stockée lors du post‑traitement (en mémoire, ou dans un petit fichier JSON ou ta base).

---

## 3. Intégration côté player MSE

Dans `hls-player.html` (ou futur player plus “pimpé”) :

1. **Au chargement d’un torrent** :
   - Tu appelles déjà `/api/mse/hls/{torrentId}` pour `playlistUrl`.
   - Tu appelles en plus `/api/mse/subtitles/{torrentId}`.
   - Tu construis une liste de sous‑titres dans l’UI (select ou menu).

2. **Activation d’une piste** :
   - Tu ajoutes dynamiquement un `<track>` sur le `<video>` :

     ```html
     <track kind="subtitles"
            srclang="fr"
            label="Français"
            src="/subtitles/xxx.vtt"
            default>
     ```

   - Ou en JS :

     ```js
     const track = document.createElement('track');
     track.kind = 'subtitles';
     track.srclang = 'fr';
     track.label = 'Français';
     track.src = subtitle.url;
     video.appendChild(track);
     ```

   - Tu gères ensuite `TextTrack.mode = 'showing' / 'hidden'` pour activer/désactiver.

3. **Plus tard : sous‑titres externes**

   Quand tu ajouteras des `.srt` téléchargés :

   - tu les traiteras comme une “source de sous‑titres supplémentaire” pour ce torrent,
   - tu les convertiras aussi en `.vtt`,
   - tu les ajouteras à la même liste côté backend et frontend.

---

## 4. Proposition pour la suite

Si cette architecture te va, on peut procéder comme ça :

1. Étendre `IVideoCodecDetector` / `VideoCodecDetector` pour renvoyer aussi les pistes de sous‑titres internes.
2. Ajouter une petite structure `SubtitleTrackInfo` dans ton domaine.
3. Ajouter dans ton pipeline de post‑traitement une étape d’extraction en `.vtt` pour 1–2 pistes de sous‑titres (les plus utiles).
4. Créer un endpoint minimal `/api/mse/subtitles/{torrentId}` et adapter le player pour charger/activer au moins une piste.

Dis-moi si tu valides cette approche “extraction interne → WebVTT → API + UI”, et on commence par l’étape 1 (détection des pistes de sous‑titres dans `VideoCodecDetector`).


### Plan concret pour la suite (backend + POC front minimal)

On part donc sur :

- **Backend** (toi) :
  - Étendre la détection pour inclure les pistes de sous‑titres internes.
  - Extraire quelques pistes en WebVTT.
  - Exposer ces pistes par torrent via une API JSON + fichiers statiques.

- **POC Front (`hls-player.html`)** :
  - Garder `hls.js` pour la vidéo.
  - Ajouter :
    - un appel à `/api/mse/subtitles/{torrentId}`,
    - un petit select `<select>` pour choisir la piste,
    - ajout dynamique de `<track>` sur le `<video>`.

---




Ok, j'ai donc bien les les Subtitles detect:
""
, il faut donc les extraire maintenant?