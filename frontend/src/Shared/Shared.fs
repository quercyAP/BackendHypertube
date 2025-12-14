namespace Shared

open System

type UserDto = {
    id: Guid
    username: string
    email: string
    firstName: string
    lastName: string
    avatarUrl: string option
    preferredLanguage: string
    createdAt: DateTime
}

type RegisterRequest = {
    username: string
    email: string
    password: string
    confirmPassword: string
    firstName: string
    lastName: string
    preferredLanguage: string
}

type LoginRequest = {
    usernameOrEmail: string
    password: string
    rememberMe: bool
}

type RefreshTokenRequest = {
    refreshToken: string
}

type ExternalLoginRequest = {
    provider: string
    providerUserId: string
    email: string
    username: string
    firstName: string
    lastName: string
    profilePictureUrl: string option
}

type ForgotPasswordRequest = {
    email: string
}

type ResetPasswordRequest = {
    email: string
    token: string
    newPassword: string
    confirmPassword: string
}

type AuthRequest =
    | LoginRequest of LoginRequest
    | RegisterRequest of RegisterRequest
    | ExternalLoginRequest of ExternalLoginRequest
    | ForgotPasswordRequest of ForgotPasswordRequest
    | ResetPasswordRequest of ResetPasswordRequest

type AuthenticationResult = {
    success: bool option
    accessToken: string option
    refreshToken: string option
    expiresAt: DateTime option
    refreshExpiresAt: DateTime option
    user: UserDto option
    errorMessage: string option
    errors: Map<string, string list> option
}

// public class TorrentOptionDto
// {
//     public string Quality { get; set; } = string.Empty;
//     public string MagnetLink { get; set; } = string.Empty;
//     public string? TorrentUrl { get; set; }
//     public long? Size { get; set; }
//     public int? Seeds { get; set; }
//     public int? Peers { get; set; }
//     public string Source { get; set; } = string.Empty;
// }

type TorrentOptionDto = {
    quality: string
    magnetLink: string
    torrentUrl: string option
    size: int64 option
    seeds: int32 option
    peers: int32 option
    source: string
}

type MovieDto = {
    id: Guid
    imdbId: string option
    title: string
    year: int option
    rating: decimal option
    genre: string option
    coverImageUrl: string option
    isWatched: bool
}

type MovieDetailsDto = {
    id: Guid
    imdbId: string option
    title: string
    year: int option
    rating: decimal option
    genre: string option
    director: string option
    cast: string option
    summary: string option
    coverImageUrl: string option
    duration: int option
    isWatched: bool
    lastWatchedAt: DateTime option
    torrents: TorrentOptionDto list
}

type MovieListResponse = {
    movies: MovieDto list
    totalPages: int32
    currentPage: int32
    totalCount: int32
    hasMore: bool
}

type MovieTrailerDto = {
    youtubeKey: string
}
