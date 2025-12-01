#!/bin/bash

# =============================================================================
# HYPERTUBE API TESTS
# =============================================================================
# Script de tests pratiques pour l'API REST Hypertube
# Teste tous les flows principaux avec gestion des tokens JWT
#
# Usage:
#   ./API_TESTS.sh                    # Exécute tous les tests
#   ./API_TESTS.sh auth               # Teste uniquement l'authentification
#   ./API_TESTS.sh movies             # Teste uniquement les films
#   ./API_TESTS.sh comments           # Teste uniquement les commentaires
#
# Prérequis:
#   - Backend lancé sur http://localhost:5000
#   - jq installé pour parser JSON
#   - curl installé
#
# =============================================================================

# Configuration
API_BASE="http://localhost:5000/api"
TEST_EMAIL="test-$(date +%s)@example.com"
TEST_USERNAME="testuser-$(date +%s)"
TEST_PASSWORD="Test123!"

# Couleurs pour l'output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

# Variables globales pour les tokens
ACCESS_TOKEN=""
REFRESH_TOKEN=""
USER_ID=""

# =============================================================================
# FONCTIONS UTILITAIRES
# =============================================================================

print_section() {
    echo -e "\n${BLUE}========================================${NC}"
    echo -e "${BLUE}$1${NC}"
    echo -e "${BLUE}========================================${NC}\n"
}

print_test() {
    echo -e "${YELLOW}▶ Test: $1${NC}"
}

print_success() {
    echo -e "${GREEN}✓ Succès: $1${NC}"
}

print_error() {
    echo -e "${RED}✗ Erreur: $1${NC}"
}

print_info() {
    echo -e "${BLUE}ℹ Info: $1${NC}"
}

check_prereqs() {
    print_section "Vérification des prérequis"

    if ! command -v curl &> /dev/null; then
        print_error "curl n'est pas installé"
        exit 1
    fi
    print_success "curl est installé"

    if ! command -v jq &> /dev/null; then
        print_error "jq n'est pas installé"
        print_info "Installation: winget install jqlang.jq"
        exit 1
    fi
    print_success "jq est installé"

    # Test si l'API est accessible
    print_info "Test de connexion à l'API..."
    if curl -s -f -o /dev/null "http://localhost:5000/api/v1/health" 2>/dev/null; then
        print_success "Backend accessible sur http://localhost:5000"
    else
        print_error "Backend non accessible"
        print_info "Lancez: docker-compose up"
        exit 1
    fi
}

# =============================================================================
# TESTS AUTHENTIFICATION
# =============================================================================

test_auth_flow() {
    print_section "Tests Authentification"

    # 1. Inscription
    print_test "Inscription d'un nouvel utilisateur"
    REGISTER_RESPONSE=$(curl -s -X POST "$API_BASE/Auth/register" \
        -H "Content-Type: application/json" \
        -d "{
            \"email\": \"$TEST_EMAIL\",
            \"username\": \"$TEST_USERNAME\",
            \"firstName\": \"Test\",
            \"lastName\": \"User\",
            \"password\": \"$TEST_PASSWORD\",
            \"confirmPassword\": \"$TEST_PASSWORD\"
        }")

    if echo "$REGISTER_RESPONSE" | jq -e '.accessToken' > /dev/null 2>&1; then
        print_success "Inscription réussie"
        ACCESS_TOKEN=$(echo "$REGISTER_RESPONSE" | jq -r '.accessToken')
        REFRESH_TOKEN=$(echo "$REGISTER_RESPONSE" | jq -r '.refreshToken')
        USER_ID=$(echo "$REGISTER_RESPONSE" | jq -r '.user.id')
        print_info "User ID: $USER_ID"
        print_info "Access Token: ${ACCESS_TOKEN:0:30}..."
    else
        print_error "Échec de l'inscription"
        echo "$REGISTER_RESPONSE" | jq '.' 2>/dev/null || echo "$REGISTER_RESPONSE"
        return 1
    fi

    # 2. Test d'une requête authentifiée
    print_test "Requête authentifiée - Récupération du profil"
    PROFILE_RESPONSE=$(curl -s -X GET "$API_BASE/users/$USER_ID" \
        -H "Authorization: Bearer $ACCESS_TOKEN")

    if echo "$PROFILE_RESPONSE" | jq -e '.id' > /dev/null 2>&1; then
        print_success "Profil récupéré avec succès"
        echo "$PROFILE_RESPONSE" | jq '{id, username, email, firstName, lastName}'
    else
        print_error "Échec de la récupération du profil"
        echo "$PROFILE_RESPONSE" | jq '.' 2>/dev/null || echo "$PROFILE_RESPONSE"
    fi

    # 3. Refresh du token
    print_test "Rafraîchissement du token"
    REFRESH_RESPONSE=$(curl -s -X POST "$API_BASE/Auth/refresh" \
        -H "Content-Type: application/json" \
        -d "{\"refreshToken\": \"$REFRESH_TOKEN\"}")

    if echo "$REFRESH_RESPONSE" | jq -e '.accessToken' > /dev/null 2>&1; then
        print_success "Token rafraîchi avec succès"
        ACCESS_TOKEN=$(echo "$REFRESH_RESPONSE" | jq -r '.accessToken')
        REFRESH_TOKEN=$(echo "$REFRESH_RESPONSE" | jq -r '.refreshToken')
        print_info "Nouveau Access Token: ${ACCESS_TOKEN:0:30}..."
    else
        print_error "Échec du rafraîchissement"
        echo "$REFRESH_RESPONSE" | jq '.' 2>/dev/null || echo "$REFRESH_RESPONSE"
    fi

    # 4. Test /me endpoint
    print_test "GET /Auth/me - Informations utilisateur courant"
    ME_RESPONSE=$(curl -s -X GET "$API_BASE/Auth/me" \
        -H "Authorization: Bearer $ACCESS_TOKEN")

    if echo "$ME_RESPONSE" | jq -e '.id' > /dev/null 2>&1; then
        print_success "Endpoint /me fonctionne"
        echo "$ME_RESPONSE" | jq '{id, username, email}'
    else
        print_error "Échec endpoint /me"
        echo "$ME_RESPONSE" | jq '.' 2>/dev/null || echo "$ME_RESPONSE"
    fi

    # 5. Test token invalide
    print_test "Test avec token invalide (doit retourner 401)"
    INVALID_RESPONSE=$(curl -s -w "\n%{http_code}" -X GET "$API_BASE/Auth/me" \
        -H "Authorization: Bearer invalid_token_123")

    HTTP_CODE=$(echo "$INVALID_RESPONSE" | tail -n1)
    if [ "$HTTP_CODE" = "401" ]; then
        print_success "Token invalide correctement rejeté (401)"
    else
        print_error "Devrait retourner 401, reçu: $HTTP_CODE"
    fi
}

# =============================================================================
# TESTS PROFIL UTILISATEUR
# =============================================================================

test_profile_flow() {
    print_section "Tests Profil Utilisateur"

    if [ -z "$ACCESS_TOKEN" ]; then
        print_error "Pas de token. Lancez d'abord: ./API_TESTS.sh auth"
        return 1
    fi

    # 1. Récupération du profil
    print_test "GET /users/{id} - Récupération du profil"
    PROFILE_RESPONSE=$(curl -s -X GET "$API_BASE/users/$USER_ID" \
        -H "Authorization: Bearer $ACCESS_TOKEN")

    if echo "$PROFILE_RESPONSE" | jq -e '.id' > /dev/null 2>&1; then
        print_success "Profil récupéré"
        echo "$PROFILE_RESPONSE" | jq '{id, username, email, firstName, lastName}'
    else
        print_error "Échec récupération profil"
        echo "$PROFILE_RESPONSE"
    fi

    # 2. Mise à jour du profil
    print_test "PATCH /users/{id} - Mise à jour du profil"
    UPDATE_RESPONSE=$(curl -s -X PATCH "$API_BASE/users/$USER_ID" \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        -H "Content-Type: application/json" \
        -d '{
            "firstName": "TestUpdated",
            "lastName": "UserUpdated"
        }')

    if echo "$UPDATE_RESPONSE" | jq -e '.id' > /dev/null 2>&1; then
        print_success "Profil mis à jour"
        echo "$UPDATE_RESPONSE" | jq '{firstName, lastName}'
    else
        print_error "Échec mise à jour"
        echo "$UPDATE_RESPONSE" | jq '.' 2>/dev/null || echo "$UPDATE_RESPONSE"
    fi

    # 3. Recherche par username
    print_test "GET /users/username/{username} - Recherche par username"
    USERNAME_RESPONSE=$(curl -s -X GET "$API_BASE/users/username/$TEST_USERNAME" \
        -H "Authorization: Bearer $ACCESS_TOKEN")

    if echo "$USERNAME_RESPONSE" | jq -e '.id' > /dev/null 2>&1; then
        print_success "Utilisateur trouvé par username"
        echo "$USERNAME_RESPONSE" | jq '{id, username}'
    else
        print_error "Échec recherche par username"
        echo "$USERNAME_RESPONSE"
    fi
}

# =============================================================================
# TESTS FILMS ET TORRENTS
# =============================================================================

test_movies_flow() {
    print_section "Tests Films et Torrents"

    if [ -z "$ACCESS_TOKEN" ]; then
        print_error "Pas de token. Lancez d'abord: ./API_TESTS.sh auth"
        return 1
    fi

    # 1. Liste des films
    print_test "GET /movies - Liste des films"
    MOVIES_RESPONSE=$(curl -s -X GET "$API_BASE/movies?page=1&pageSize=5" \
        -H "Authorization: Bearer $ACCESS_TOKEN")

    if echo "$MOVIES_RESPONSE" | jq -e '.' > /dev/null 2>&1; then
        MOVIE_COUNT=$(echo "$MOVIES_RESPONSE" | jq '. | if type == "array" then length else .items // [] | length end')
        print_success "Films récupérés: $MOVIE_COUNT"
    else
        print_error "Échec récupération films"
        echo "$MOVIES_RESPONSE"
    fi

    # 2. Recherche de films
    print_test "GET /movies/search - Recherche de films"
    SEARCH_RESPONSE=$(curl -s -X GET "$API_BASE/movies/search?query=matrix&page=1&pageSize=3" \
        -H "Authorization: Bearer $ACCESS_TOKEN")

    if echo "$SEARCH_RESPONSE" | jq -e '.' > /dev/null 2>&1; then
        print_success "Recherche effectuée"
        echo "$SEARCH_RESPONSE" | jq '.[0] | {title, year, imdbId}' 2>/dev/null || echo "Aucun résultat"
    else
        print_error "Échec recherche"
        echo "$SEARCH_RESPONSE"
    fi

    # 3. Recherche de torrents en live
    print_test "GET /movies/search-torrents - Recherche live de torrents"
    TORRENT_SEARCH=$(curl -s -X GET "$API_BASE/movies/search-torrents?query=inception&page=1&pageSize=3" \
        -H "Authorization: Bearer $ACCESS_TOKEN")

    MOVIE_ID=""
    if echo "$TORRENT_SEARCH" | jq -e '.' > /dev/null 2>&1; then
        RESULT_COUNT=$(echo "$TORRENT_SEARCH" | jq '. | if type == "array" then length else .results // [] | length end')
        print_success "Torrents trouvés: $RESULT_COUNT"

        # Essayer d'extraire un ID de film
        MOVIE_ID=$(echo "$TORRENT_SEARCH" | jq -r '.[0].imdbId // .[0].id // .results[0].imdbId // empty' 2>/dev/null)
        if [ -n "$MOVIE_ID" ]; then
            print_info "Movie ID: $MOVIE_ID"
        fi
    else
        print_error "Échec recherche torrents"
        echo "$TORRENT_SEARCH"
    fi

    # 4. Détails d'un film (si on a un ID)
    if [ -n "$MOVIE_ID" ] && [ "$MOVIE_ID" != "null" ]; then
        print_test "GET /movies/{id} - Détails du film"
        MOVIE_DETAILS=$(curl -s -X GET "$API_BASE/movies/$MOVIE_ID" \
            -H "Authorization: Bearer $ACCESS_TOKEN")

        if echo "$MOVIE_DETAILS" | jq -e '.title' > /dev/null 2>&1; then
            print_success "Détails récupérés"
            echo "$MOVIE_DETAILS" | jq '{title, year, rating}'
        else
            print_info "Film pas encore en base (normal si pas téléchargé)"
        fi
    fi

    # 5. Liste des torrents actifs
    print_test "GET /torrents - Liste des torrents"
    TORRENTS_LIST=$(curl -s -X GET "$API_BASE/torrents" \
        -H "Authorization: Bearer $ACCESS_TOKEN")

    if echo "$TORRENTS_LIST" | jq -e '.' > /dev/null 2>&1; then
        TORRENT_COUNT=$(echo "$TORRENTS_LIST" | jq 'if type == "array" then length else 0 end')
        print_success "Torrents actifs: $TORRENT_COUNT"
    else
        print_error "Échec récupération torrents"
        echo "$TORRENTS_LIST"
    fi
}

# =============================================================================
# TESTS COMMENTAIRES
# =============================================================================

test_comments_flow() {
    print_section "Tests Commentaires"

    if [ -z "$ACCESS_TOKEN" ]; then
        print_error "Pas de token. Lancez d'abord: ./API_TESTS.sh auth"
        return 1
    fi

    # 1. Récupérer un film existant depuis la base de données
    print_test "Récupération d'un film existant pour les tests"
    MOVIES_RESPONSE=$(curl -s -X GET "$API_BASE/movies?page=1&pageSize=1" \
        -H "Authorization: Bearer $ACCESS_TOKEN")

    MOVIE_ID=""
    # Essayer d'extraire un ID de film depuis la liste
    if echo "$MOVIES_RESPONSE" | jq -e '.' > /dev/null 2>&1; then
        MOVIE_ID=$(echo "$MOVIES_RESPONSE" | jq -r '.[0].id // .items[0].id // empty' 2>/dev/null)
    fi

    if [ -z "$MOVIE_ID" ] || [ "$MOVIE_ID" = "null" ]; then
        print_info "Aucun film en base de données"
        print_info "Les tests de création/modification de commentaires seront ignorés"
        print_info "Pour tester complètement, ajoutez un film en base d'abord"

        # Utiliser un GUID aléatoire pour les tests de lecture (retournera liste vide)
        MOVIE_ID="00000000-0000-0000-0000-000000000001"
    else
        print_success "Film trouvé: $MOVIE_ID"
    fi

    # 2. Récupérer les commentaires existants (fonctionne même sans film)
    print_test "GET /movies/{movieId}/comments - Liste des commentaires"
    COMMENTS_RESPONSE=$(curl -s -X GET "$API_BASE/movies/$MOVIE_ID/comments" \
        -H "Authorization: Bearer $ACCESS_TOKEN")

    if echo "$COMMENTS_RESPONSE" | jq -e '.' > /dev/null 2>&1; then
        COMMENT_COUNT=$(echo "$COMMENTS_RESPONSE" | jq 'if type == "array" then length else 0 end')
        print_success "Commentaires récupérés: $COMMENT_COUNT"
    else
        print_error "Échec récupération commentaires"
        echo "$COMMENTS_RESPONSE"
    fi

    # 3. Vérifier si on a un vrai film pour les tests d'écriture
    MOVIE_EXISTS=$(curl -s -X GET "$API_BASE/movies/$MOVIE_ID" \
        -H "Authorization: Bearer $ACCESS_TOKEN")

    HAS_REAL_MOVIE="false"
    if echo "$MOVIE_EXISTS" | jq -e '.id' > /dev/null 2>&1; then
        HAS_REAL_MOVIE="true"
    fi

    if [ "$HAS_REAL_MOVIE" = "true" ]; then
        # 4. Ajouter un commentaire (seulement si film existe)
        print_test "POST /movies/{movieId}/comments - Ajouter un commentaire"
        COMMENT_TEXT="Test automatique $(date +%H:%M:%S)"
        ADD_COMMENT=$(curl -s -X POST "$API_BASE/movies/$MOVIE_ID/comments" \
            -H "Authorization: Bearer $ACCESS_TOKEN" \
            -H "Content-Type: application/json" \
            -d "{\"content\": \"$COMMENT_TEXT\"}")

        COMMENT_ID=""
        if echo "$ADD_COMMENT" | jq -e '.id' > /dev/null 2>&1; then
            print_success "Commentaire ajouté"
            COMMENT_ID=$(echo "$ADD_COMMENT" | jq -r '.id')
            echo "$ADD_COMMENT" | jq '{id, content}'
            print_info "Comment ID: $COMMENT_ID"
        else
            print_error "Échec ajout commentaire"
            echo "$ADD_COMMENT" | jq '.' 2>/dev/null || echo "$ADD_COMMENT"
        fi

        # 5. Modifier le commentaire
        if [ -n "$COMMENT_ID" ] && [ "$COMMENT_ID" != "null" ]; then
            print_test "PATCH /comments/{id} - Modifier le commentaire"
            UPDATE_COMMENT=$(curl -s -X PATCH "$API_BASE/comments/$COMMENT_ID" \
                -H "Authorization: Bearer $ACCESS_TOKEN" \
                -H "Content-Type: application/json" \
                -d "{\"content\": \"Modifié $(date +%H:%M:%S)\"}")

            if echo "$UPDATE_COMMENT" | jq -e '.id' > /dev/null 2>&1; then
                print_success "Commentaire modifié"
                echo "$UPDATE_COMMENT" | jq '{id, content}'
            else
                print_error "Échec modification"
                echo "$UPDATE_COMMENT" | jq '.' 2>/dev/null || echo "$UPDATE_COMMENT"
            fi

            # 6. Supprimer le commentaire
            print_test "DELETE /comments/{id} - Supprimer le commentaire"
            DELETE_RESPONSE=$(curl -s -w "\n%{http_code}" -X DELETE "$API_BASE/comments/$COMMENT_ID" \
                -H "Authorization: Bearer $ACCESS_TOKEN")

            HTTP_CODE=$(echo "$DELETE_RESPONSE" | tail -n1)
            if [ "$HTTP_CODE" = "204" ] || [ "$HTTP_CODE" = "200" ]; then
                print_success "Commentaire supprimé"
            else
                print_error "Échec suppression (HTTP $HTTP_CODE)"
            fi
        fi
    else
        print_info "SKIP: Tests CRUD commentaires (aucun film en base)"
        print_success "Tests CRUD commentaires ignorés (pas de données de test)"
    fi

    # 7. Test sans authentification (utilise un GUID valide)
    print_test "POST sans token (doit retourner 401)"
    NO_AUTH=$(curl -s -w "\n%{http_code}" -X POST "$API_BASE/movies/00000000-0000-0000-0000-000000000001/comments" \
        -H "Content-Type: application/json" \
        -d "{\"content\": \"Test sans auth\"}")

    HTTP_CODE=$(echo "$NO_AUTH" | tail -n1)
    if [ "$HTTP_CODE" = "401" ]; then
        print_success "Requête non authentifiée rejetée (401)"
    else
        print_error "Devrait retourner 401, reçu: $HTTP_CODE"
    fi
}

# =============================================================================
# TESTS SOUS-TITRES
# =============================================================================

test_subtitles_flow() {
    print_section "Tests Sous-titres"

    if [ -z "$ACCESS_TOKEN" ]; then
        print_error "Pas de token. Lancez d'abord: ./API_TESTS.sh auth"
        return 1
    fi

    # Récupérer un film existant depuis la base de données
    print_test "Récupération d'un film existant pour les tests"
    MOVIES_RESPONSE=$(curl -s -X GET "$API_BASE/movies?page=1&pageSize=1" \
        -H "Authorization: Bearer $ACCESS_TOKEN")

    MOVIE_ID=""
    if echo "$MOVIES_RESPONSE" | jq -e '.' > /dev/null 2>&1; then
        MOVIE_ID=$(echo "$MOVIES_RESPONSE" | jq -r '.[0].id // .items[0].id // empty' 2>/dev/null)
    fi

    if [ -z "$MOVIE_ID" ] || [ "$MOVIE_ID" = "null" ]; then
        print_info "Aucun film en base de données"
        print_info "Les tests de sous-titres nécessitent un film existant"
        print_success "Tests sous-titres ignorés (pas de données de test)"
        return 0
    fi

    print_success "Film trouvé: $MOVIE_ID"

    # 1. Rechercher des sous-titres disponibles
    print_test "GET /movies/{id}/subtitles/available - Sous-titres disponibles"
    SUBTITLES=$(curl -s -X GET "$API_BASE/movies/$MOVIE_ID/subtitles/available" \
        -H "Authorization: Bearer $ACCESS_TOKEN")

    if echo "$SUBTITLES" | jq -e '.' > /dev/null 2>&1; then
        # Check for languages array or message
        if echo "$SUBTITLES" | jq -e '.languages' > /dev/null 2>&1; then
            SUB_COUNT=$(echo "$SUBTITLES" | jq '.languages | length')
            print_success "Sous-titres trouvés: $SUB_COUNT langues"
        else
            print_success "Réponse reçue (format variable)"
            echo "$SUBTITLES" | jq '.' 2>/dev/null || echo "$SUBTITLES"
        fi
    else
        print_error "Échec recherche sous-titres"
        echo "$SUBTITLES"
    fi
}

# =============================================================================
# MAIN
# =============================================================================

main() {
    echo -e "${GREEN}"
    echo "╔═══════════════════════════════════════════════════════════════╗"
    echo "║           HYPERTUBE API TESTS - Suite de Tests               ║"
    echo "╚═══════════════════════════════════════════════════════════════╝"
    echo -e "${NC}"

    check_prereqs

    case "${1:-all}" in
        auth)
            test_auth_flow
            ;;
        profile)
            test_auth_flow
            test_profile_flow
            ;;
        movies)
            test_auth_flow
            test_movies_flow
            ;;
        comments)
            test_auth_flow
            test_comments_flow
            ;;
        subtitles)
            test_auth_flow
            test_subtitles_flow
            ;;
        all)
            test_auth_flow
            test_profile_flow
            test_movies_flow
            test_comments_flow
            test_subtitles_flow
            ;;
        *)
            echo "Usage: $0 {all|auth|profile|movies|comments|subtitles}"
            exit 1
            ;;
    esac

    print_section "Tests terminés"
    echo ""
    echo -e "${GREEN}Tokens pour utilisation manuelle:${NC}"
    echo "export ACCESS_TOKEN=\"$ACCESS_TOKEN\""
    echo "export USER_ID=\"$USER_ID\""
}

main "$@"
