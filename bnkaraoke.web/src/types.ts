// src/types.ts
export interface Song {
  id: number;
  title: string;
  artist: string;
  genre?: string | null;
  decade?: string | null;
  bpm?: number;
  danceability?: number;
  energy?: number;
  mood?: string | null;
  popularity?: number;
  spotifyId?: string | null;
  youTubeUrl?: string | null;
  status?: string;
  approvedBy?: string | null;
  mature?: boolean | null;
  requestDate?: string | null;
  requestedBy?: string | null;
  approvedDate?: string | null;
  musicBrainzId?: string | null;
  lastFmPlaycount?: number | null;
  valence?: number;
  normalizationGain?: number | null;
  fadeStartTime?: number | null;
  introMuteDuration?: number | null;
  cached?: boolean | null;
  serverCached?: boolean | null;
  analyzed?: boolean | null;
}

const isPresent = (value: unknown): boolean => {
  if (value === undefined || value === null) {
    return false;
  }
  if (typeof value === "string") {
    const trimmed = value.trim();
    if (!trimmed) {
      return false;
    }
    const lower = trimmed.toLowerCase();
    return lower !== "error" && lower !== "unknown" && lower !== "null" && lower !== "undefined";
  }
  return true;
};

const coalesce = (...values: unknown[]): unknown => values.find(isPresent);

const sanitizeString = (value: unknown): string | undefined => {
  if (!isPresent(value)) {
    return undefined;
  }
  if (typeof value === "string") {
    return value.trim();
  }
  if (typeof value === "number" || typeof value === "boolean") {
    return value.toString();
  }
  return undefined;
};

const toNumber = (value: unknown): number | undefined => {
  if (!isPresent(value)) {
    return undefined;
  }
  if (typeof value === "number") {
    return Number.isFinite(value) ? value : undefined;
  }
  if (typeof value === "string") {
    const parsed = Number(value.trim());
    return Number.isFinite(parsed) ? parsed : undefined;
  }
  return undefined;
};

const toNullableNumber = (value: unknown): number | null | undefined => {
  if (value === undefined) {
    return undefined;
  }
  if (value === null) {
    return null;
  }
  return toNumber(value);
};

const toBoolean = (value: unknown): boolean | undefined => {
  if (!isPresent(value)) {
    return undefined;
  }
  if (typeof value === "boolean") {
    return value;
  }
  if (typeof value === "number") {
    return value !== 0;
  }
  if (typeof value === "string") {
    const normalized = value.trim().toLowerCase();
    if (["true", "yes", "y", "1"].includes(normalized)) {
      return true;
    }
    if (["false", "no", "n", "0"].includes(normalized)) {
      return false;
    }
  }
  return undefined;
};

export const normalizeSong = (rawInput: unknown): Song => {
  const raw = (rawInput ?? {}) as Partial<Song>;
  const source = (rawInput ?? {}) as Record<string, unknown>;

  const idValue = coalesce(raw.id, source.Id, source.songId, source.SongId);
  const parsedId = toNumber(idValue);

  const normalized: Song = {
    id: parsedId !== undefined ? parsedId : 0,
    title: sanitizeString(coalesce(raw.title, source.Title)) ?? "Unknown Title",
    artist: sanitizeString(coalesce(raw.artist, source.Artist, source.singer, source.Singer)) ?? "Unknown Artist",
  };

  const genre = sanitizeString(coalesce(raw.genre, source.Genre));
  if (genre !== undefined) normalized.genre = genre;

  const decade = sanitizeString(coalesce(raw.decade, source.Decade));
  if (decade !== undefined) normalized.decade = decade;

  const bpm = toNumber(coalesce(raw.bpm, source.Bpm));
  if (bpm !== undefined) normalized.bpm = bpm;

  const danceability = toNumber(coalesce(raw.danceability, source.Danceability));
  if (danceability !== undefined) normalized.danceability = danceability;

  const energy = toNumber(coalesce(raw.energy, source.Energy));
  if (energy !== undefined) normalized.energy = energy;

  const mood = sanitizeString(coalesce(raw.mood, source.Mood));
  if (mood !== undefined) normalized.mood = mood;

  const popularity = toNumber(coalesce(raw.popularity, source.Popularity));
  if (popularity !== undefined) normalized.popularity = popularity;

  const spotifyId = sanitizeString(coalesce(raw.spotifyId, source.SpotifyId));
  if (spotifyId !== undefined) normalized.spotifyId = spotifyId;

  const youtubeUrl = sanitizeString(coalesce(raw.youTubeUrl, source.YouTubeUrl, source.youtubeUrl));
  if (youtubeUrl !== undefined) normalized.youTubeUrl = youtubeUrl;

  const status = sanitizeString(coalesce(raw.status, source.Status));
  if (status !== undefined) normalized.status = status;

  const approvedBy = sanitizeString(coalesce(raw.approvedBy, source.ApprovedBy));
  if (approvedBy !== undefined) normalized.approvedBy = approvedBy;

  const mature = toBoolean(coalesce(raw.mature, source.Mature));
  if (mature !== undefined) normalized.mature = mature;

  const requestDate = sanitizeString(coalesce(raw.requestDate, source.RequestDate));
  if (requestDate !== undefined) normalized.requestDate = requestDate;

  const requestedBy = sanitizeString(coalesce(raw.requestedBy, source.RequestedBy));
  if (requestedBy !== undefined) normalized.requestedBy = requestedBy;

  const approvedDate = sanitizeString(coalesce(raw.approvedDate, source.ApprovedDate));
  if (approvedDate !== undefined) normalized.approvedDate = approvedDate;

  const musicBrainzId = sanitizeString(coalesce(raw.musicBrainzId, source.MusicBrainzId));
  if (musicBrainzId !== undefined) normalized.musicBrainzId = musicBrainzId;

  const lastFmPlaycount = toNumber(coalesce(raw.lastFmPlaycount, source.LastFmPlaycount));
  if (lastFmPlaycount !== undefined) normalized.lastFmPlaycount = lastFmPlaycount;

  const valence = toNumber(coalesce(raw.valence, source.Valence));
  if (valence !== undefined) normalized.valence = valence;

  const normalizationGain = toNullableNumber(coalesce(raw.normalizationGain, source.NormalizationGain));
  if (normalizationGain !== undefined) normalized.normalizationGain = normalizationGain;

  const fadeStartTime = toNullableNumber(coalesce(raw.fadeStartTime, source.FadeStartTime));
  if (fadeStartTime !== undefined) normalized.fadeStartTime = fadeStartTime;

  const introMuteDuration = toNullableNumber(coalesce(raw.introMuteDuration, source.IntroMuteDuration));
  if (introMuteDuration !== undefined) normalized.introMuteDuration = introMuteDuration;

  const cached = toBoolean(coalesce(raw.cached, source.Cached));
  if (cached !== undefined) normalized.cached = cached;

  const serverCached = toBoolean(coalesce(raw.serverCached, source.ServerCached, source.isServerCached, source.IsServerCached));
  if (serverCached !== undefined) normalized.serverCached = serverCached;

  const analyzed = toBoolean(coalesce(raw.analyzed, source.Analyzed));
  if (analyzed !== undefined) normalized.analyzed = analyzed;

  return normalized;
};

export interface SpotifySong {
  id: string;
  title: string;
  artist: string;
  genre?: string;
  popularity?: number;
  bpm?: number;
  energy?: number;
  valence?: number;
  danceability?: number;
  decade?: string;
}

export interface QueueItem {
  id: number;
  title: string;
  artist: string;
  status: string;
  singers: string[];
  requests: { forWhom: string }[];
}

export interface Event {
  eventId: number;
  eventCode: string;
  description: string;
  status: string;
  visibility: string;
  location: string;
  scheduledDate: string;
  scheduledStartTime?: string;
  scheduledEndTime?: string;
  karaokeDJName?: string;
  isCanceled: boolean;
  queueCount: number;
  requestLimit: number;
}

export interface EventQueueItemResponse {
  queueId: number;
  eventId: number;
  songId: number;
  songTitle: string;
  songArtist: string;
  youTubeUrl?: string;
  requestorUserName: string;
  requestorDisplayName: string;
  singers: string;
  position: number;
  status: string;
  isActive: boolean;
  wasSkipped: boolean;
  isCurrentlyPlaying: boolean;
  sungAt?: string;
  isOnBreak: boolean;
  holdReason: string;
  isUpNext: boolean;
  isSingerLoggedIn: boolean;
  isSingerJoined: boolean;
  isSingerOnBreak: boolean;
}

export interface EventQueueItem {
  queueId: number;
  eventId: number;
  songId: number;
  requestorUserName: string;
  requestorFullName: string | null; // Added for RequestorFullName
  singers: string[];
  position: number;
  status: string;
  isActive: boolean;
  wasSkipped: boolean;
  isCurrentlyPlaying: boolean;
  sungAt?: string;
  isOnBreak: boolean;
  isUpNext: boolean;
  songTitle?: string;
  songArtist?: string;
  youTubeUrl?: string;
  isServerCached: boolean;
}

export interface AttendanceAction {
  RequestorId: string;
}

export interface User {
  firstName: string;
  lastName: string;
}