# État des lieux des fournisseurs de torrents

## Contexte général

- **Endpoint concerné** : `GET /api/movies/active`
- **Objectif** : afficher une liste de films ayant des torrents actifs, triés par activité (seeders), avec un "bestTorrent" par film.
- **Architecture** :
  - `MoviesController.GetMoviesWithActiveTorrents` appelle `ITorrentSearchService.GetPopularAsync`.
  - `TorrentSearchService.GetPopularAsync` interroge plusieurs fournisseurs :
    - `YtsService` (YTS)
    - `PirateBayService` (PirateBay / Apibay)
  - Les résultats sont fusionnés, dédupliqués, filtrés, puis renvoyés.

---

## Fonctionnement actuel de /api/movies/active

### Côté contrôleur

`MoviesController.GetMoviesWithActiveTorrents` :

- Paramètres : `page`, `pageSize`.
- Appelle :
  - `_torrentSearchService.GetPopularAsync(page, pageSize, cancellationToken)`
- Traite le résultat :
  - Trie les torrents par nombre de seeders décroissant.
  - Construit un objet `movies` avec :
    - Métadonnées film : `imdbId`, `title`, `year`, `rating`, `genre`, `coverImageUrl`.
    - `bestTorrent` : `quality`, `magnetLink`, `torrentUrl`, `size`, `seeds`, `peers`, `source`.
  - Renvoyé dans un wrapper avec `totalPages`, `currentPage`, `totalCount`, `hasMore`.

### Côté service TorrentSearchService

`TorrentSearchService.GetPopularAsync` :

1. Interroge les fournisseurs en parallèle (page logique 1, limite large pour filtrage) :
   - `YtsService.GetPopularAsync(1, 100, ...)`
   - `PirateBayService.GetPopularAsync(1, 100, ...)`
2. Récupère les résultats :
   - `ytsResults`
   - `tpbResults`
3. Enrichit les résultats PirateBay avec des métadonnées (TMDB, etc.).
4. Fusionne YTS + TPB dans `allResults`.
5. Déduplication :
   - Regroupement par `ImdbId` (si présent) ou par `Title + Year`.
   - Préférence donnée à YTS pour les métadonnées, sinon à tout résultat ayant des `CoverImageUrl`.
6. Filtre global sur l’activité :
   - `const int minSeeds = 5;`
   - Ne garde que les torrents avec `Seeds >= 5`.
7. Tri et pagination :
   - Tri par `Rating` décroissant puis `Seeds` décroissant.
   - Application de `Skip / Take` pour `page` et `limit`.

**Conséquence :** même si plusieurs torrents existent pour un même film, un seul `TorrentSearchResultDto` est renvoyé par film à ce niveau (la page de détails gère les autres torrents).

---

## Problème YTS

### Symptomatique côté logs

- Exemple de log observé lors d’un appel à `/api/movies/active` :

  - `Found 0 popular from YTS and 35 from TPB, returning 34 of 34`

- Et stacktrace explicite :

  ```text
  System.Net.Http.HttpRequestException: Name or service not known (yts.mx:443)
  ...
  [ERR] Error getting popular movies from YTS
  System.Net.Http.HttpRequestException: Name or service not known (yts.mx:443)
  ```

### Diagnostic réseau

- Tests effectués sur l’hôte :

  ```bash
  nslookup yts.mx
  curl -I https://yts.mx
  ```

- Résultat :

  ```text
  ** server can't find yts.mx: NXDOMAIN
  curl: (6) Could not resolve host: yts.mx
  ```

### Conclusion

- Le problème n’est **pas** dans la logique applicative (mapping JSON, filtres, etc.).
- Le backend ne parvient pas à résoudre le nom de domaine **`yts.mx`** (erreur DNS / NXDOMAIN), à la fois :
  - Dans le container Docker (logs backend).
  - Sur la machine hôte (tests `nslookup` / `curl`).
- `YtsService.GetPopularAsync` attrape l’exception et renvoie une **liste vide**, tout en loggant une erreur.

**Impact fonctionnel :**

- `ytsResults.Count == 0` en permanence.
- La fusion YTS + PirateBay contient **uniquement des résultats PirateBay**.
- `/api/movies/active` renvoie donc seulement des torrents dont `bestTorrent.source == "PirateBay"`.

En pratique, YTS est **indisponible** pour ce projet tant que la résolution DNS de `yts.mx` n’est pas rétablie.

---

## Fonctionnement détaillé de PirateBayService

### Endpoints utilisés

1. **Recherche** (`SearchAsync`) :

   ```csharp
   // TPB API: /q.php?q=query&cat=201 (201 = Movies category)
   var response = await _httpClient.GetAsync(
       $"/q.php?q={Uri.EscapeDataString(query)}&cat=201",
       cancellationToken
   );
   ```

   - Retour JSON brut de PirateBay / Apibay.
   - Pagination gérée **côté backend** par `Skip / Take`.

2. **Populaires** (`GetPopularAsync`) :

   ```csharp
   // Get popular movies (cat 201)
   var response = await _httpClient.GetAsync(
       $"/precompiled/data_top100_201.json",
       cancellationToken
   );
   ```

   - Un seul endpoint : `data_top100_201.json` (Top 100 films).
   - Le service charge **toute la liste**, puis :
     - Mappe en `TorrentSearchResultDto`.
     - Applique des filtres et de la pagination côté C#.

### Filtres spécifiques PirateBay

Dans `MapTpbTorrentsToDto` :

- **Seeds** :
  - Ignore les torrents avec `torrent.Seeders <= 0`.
- **Packs / multi-films** :
  - Ignore si `torrent.NumFiles > 10`.
- **Codecs / formats exclus** :
  - HEVC / x265 :
    - `if (lowerName.Contains("x265") || lowerName.Contains("hevc")) continue;`
  - Encodes jugés incompatibles / mauvaise qualité streaming :
    - `xvid`, `divx`, `dvdscr`, `cam`, `telesync`, `ts`.
- **Packs / collections / saisons / compilations** :
  - Ignore si titre contient : `pack`, `collection`, `complete`, `season`, `trilogy`.
  - Ignore les motifs type : `"3x"`, `"101 movies"`, etc.

Ensuite, chaque torrent admissible est converti en `TorrentSearchResultDto` avec :

- `Title` nettoyé.
- `Year` estimée à partir du titre.
- `Quality` extraite du titre.
- `MagnetLink` construit à partir de l’infohash.
- `TorrentUrl` construit via `itorrents.net`.
- `Seeds` et `Peers` mappés.
- `Source = "PirateBay"`.

### Filtre global sur les seeders (tous providers)

Dans `TorrentSearchService.GetPopularAsync` :

- Application d’un filtre global **après fusion YTS + PirateBay** :

  ```csharp
  const int minSeeds = 5;
  var filteredResults = deduplicatedResults
      .Where(r => (r.Seeds ?? 0) >= minSeeds)
      .ToList();
  ```

- Objectif : ne conserver que des torrents **raisonnablement actifs**.

---

## Conséquences pour l’API / Expérience utilisateur

1. **YTS est effectivement inopérant** dans l’état actuel de l’infra :
   - Tous les endpoints qui dépendent de YTS pour les populaires retournent **0 résultat YTS**.
   - `/api/movies/active` fonctionne, mais se base **uniquement sur PirateBay**.

2. **Les données renvoyées par `/api/movies/active` sont :**
   - Les ~100 films les plus populaires (catégorie 201) vus par PirateBay / Apibay.
   - Filtrés pour éliminer :
     - Torrents sans seeders, packs, encodes HEVC/x265, Xvid/DivX, CAM/TS, etc.
     - Torrents avec moins de 5 seeders (filtre global `minSeeds`).

3. **Le projet est techniquement prêt à supporter plusieurs providers** (YTS, PirateBay, d’autres), mais :
   - L’indisponibilité DNS de YTS fait que la partie multi-provider est actuellement "dégradée" en mono-provider (PirateBay seul).

---

## Pistes d’évolution

### 1. Gestion explicite de l’indisponibilité YTS

- Côté infra :
  - Corriger la résolution DNS de `yts.mx` (si possible).
  - Ou considérer YTS comme définitivement indisponible dans ce contexte (blocage réseau, filtrage, etc.).
- Côté code :
  - Optionnel : exposer dans les réponses un indicateur d’état des providers (ex. `providers: { yts: "down", pirateBay: "ok" }`).
  - Prévoir un mécanisme de **désactivation propre** de YTS (flag de config) pour éviter l’illusion qu’il est actif.

### 2. Consolider l’usage de PirateBay

- Continuer à utiliser `/precompiled/data_top100_201.json` comme source principale pour les populaires.
- Éventuellement, explorer d’autres endpoints / listes précompilées (si disponibles) pour :
  - Étendre le catalogue au-delà du top 100.
  - Varier les critères (par ex. top sur 48h, etc., selon ce que propose l’API Apibay).

### 3. Ajouter un autre provider à la place de YTS

- Évaluer des alternatives publiques (exemples : EZTV pour les séries, d’autres index publics pour les films).
- Implémenter un nouveau service sur le modèle existant :
  - `EztvService` ou similaire dans `ExternalServices`.
  - Méthodes : `SearchAsync` et `GetPopularAsync` renvoyant `TorrentSearchResultDto` avec `Source = "NomDuProvider"`.
  - Intégration dans `TorrentSearchService` en lieu et place de `YtsService`.

---

## Résumé

- **YTS** :
  - Injoignable (`Name or service not known (yts.mx:443)`) → aucune donnée YTS utilisée.
- **PirateBay** :
  - Fournisseur pleinement opérationnel pour la recherche et les populaires (`data_top100_201.json`).
  - Filtres stricts pour garantir des torrents plutôt adaptés au streaming (nombre de seeders, pas de CAM/TS, pas de HEVC/x265, etc., sauf si la politique est revue).
- **/api/movies/active** :
  - Fonctionne, mais de fait **mono-source PirateBay** tant que YTS reste hors service.

Ce document peut servir de base pour décider soit de :

- Restaurer l’accès YTS (si possible),
- Remplacer YTS par un autre fournisseur,
- Ou assumer officiellement que PirateBay est la seule source de torrents exploitée par l’API, en adaptant la communication et les configs en conséquence.
