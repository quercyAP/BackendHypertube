# Plan de travail – Streaming avancé & MediaStream API

## 1. Objectifs généraux

- Améliorer l’UX du player pour le streaming HTTP Range actuel.
- Garantir des comportements propres pour :
  - Pause / reprise au même endroit.
  - Navigation (seek) dans la vidéo via la barre de progression.
- Préparer puis implémenter un player basé sur MediaSource / MediaStream API comme **bonus**.

---

## 2. Optimisation du player HTML5 actuel (prioritaire)

### 2.1. Pause / reprise fiable

- Vérifier le comportement actuel du `<video>` avec le flux HTTP Range existant.
- S’assurer que :
  - Un `pause()` puis `play()` reprend bien au même `currentTime`.
  - Aucune requête bizarre côté backend (ex: restart à 0 non désiré).
- Ajouter, si besoin :
  - Un petit indicateur de position courante pour debug.
  - Optionnel : mémorisation de la dernière position (localStorage) par torrent.

### 2.2. Seek / navigation dans la vidéo

- Vérifier que le player supporte bien les sauts de position (clic sur la barre) :
  - HTML5 `<video>` déclenche des requêtes Range appropriées vers `/api/torrents/{id}/stream`.
  - Le backend doit supporter des `Range` non linéaires (sauts vers l’avant / l’arrière).
- Tester des scénarios :
  - Aller directement à +18 min alors que seule une petite partie est téléchargée.
  - Faire plusieurs seeks rapides.
- Si nécessaire, ajuster :
  - La gestion des `Range` côté backend (validation, logs pour debug).
  - Les messages d’erreur éventuels côté front (afficher un feedback si le navigateur échoue à lire).

### 2.3. Améliorations UX simples du player

- Indicateur clair d’état :
  - `Downloading`, `Remuxing`, `Ready`, `Incompatible`, `CodecUnknown`.
- Boutons :
  - Laisser les contrôles natifs (`controls`) pour pause/play/seek.
  - Optionnel : ajouter un bouton "Recommencer depuis le début".
- (Optionnel) Sauvegarde de la progression de visionnage par utilisateur :
  - Phase 1 : `localStorage` par navigateur.
  - Phase 2 : stockage en base côté backend (lié au profil).

---

## 3. Passage progressif à MediaStream API (bonus)

### 3.1. Stratégie

- Ne commencer le travail MediaStream **qu’une fois** le streaming HTTP Range classique propre et fiable.
- Utiliser les fichiers déjà convertis/remuxés (MP4 / WebM) comme source pour le pipeline MSE.

### 3.2. Étapes techniques

1. **Prototype local simple** (sans tout intégrer au front principal) :
   - Une page de test qui :
     - Récupère un fichier MP4/WEBM depuis le backend.
     - Utilise `MediaSource` + `SourceBuffer` pour pousser les segments au player.
2. **Intégration avec le backend existant** :
   - Réutiliser `/api/torrents/{id}/stream` ou exposer un endpoint dédié aux segments.
   - Découper le fichier en chunks (sideserver) ou laisser le navigateur gérer via Range + `fetch` et pousser dans MSE.
3. **Intégration dans l’UI Hypertube** :
   - Ajouter un mode "MediaStream" pour certains torrents (par ex. un toggle ou un nouveau player pour la démo).
   - Gérer les mêmes comportements qu’en HTML5 natif : pause, reprise, seek.

### 3.3. Validation pour le bonus

- Préparer un petit scénario de démo pour l’évaluation :
  - Montrer la différence entre lecture classique et lecture via MediaStream.
  - Expliquer le pipeline : téléchargement torrent → fichier sur disque → segments MSE → player.

---

## 4. Ordre recommandé

1. **Optimiser le player HTML5 actuel (Section 2)**
   - Vérifier pause/reprise et seek.
   - Stabiliser les statuts et le feedback utilisateur.
2. **Ensuite seulement : attaquer MediaStream API (Section 3)**
   - Prototype de page de test.
   - Puis intégration dans l’UI principale une fois le prototype validé.
