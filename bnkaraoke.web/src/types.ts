// src/types.ts
export interface Song {
  id: number;
  title: string;
    artist: string;
    genre?: string;
    youTubeUrl?: string;
    status?: string;
    approvedBy?: string;
    mature?: boolean;
  bpm?: number;
  popularity?: number;
  requestDate?: string | null;
  requestedBy?: string | null;
  approvedDate?: string | null;
  spotifyId?: string;
  valence?: number;
  decade?: string;
  musicBrainzId?: string;
  mood?: string;
  lastFmPlaycount?: number;
  danceability?: number;
  energy?: number;
}

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