// src/components/SongDetailsModal.tsx
import React, { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import './SongDetailsModal.css';
import { Song, AttendanceAction, SpotifySong, normalizeSong } from '../types';
import { API_ROUTES } from '../config/apiConfig';
import { useEventContext } from "../context/EventContext";

interface SongDetailsModalProps {
  song: Song;
  isFavorite: boolean;
  isInQueue: boolean;
  onClose: () => void;
  onToggleFavorite?: (song: Song) => Promise<void>;
  onAddToQueue?: (song: Song, eventId: number) => Promise<void>;
  onDeleteFromQueue?: (eventId: number, queueId: number) => Promise<void>;
  onRequestSong?: (song: SpotifySong) => Promise<void>;
  eventId?: number;
  queueId?: number;
  readOnly?: boolean;
  checkedIn?: boolean;
  isCurrentEventLive?: boolean;
}

const SongDetailsModal: React.FC<SongDetailsModalProps> = ({
  song,
  isFavorite,
  isInQueue,
  onClose,
  onToggleFavorite,
  onAddToQueue,
  onDeleteFromQueue,
  onRequestSong,
  eventId,
  queueId,
  readOnly = false,
  checkedIn,
  isCurrentEventLive,
}) => {
  const navigate = useNavigate();
  const {
    currentEvent,
    setCurrentEvent,
    setCheckedIn,
    setIsCurrentEventLive,
    liveEvents,
    upcomingEvents,
    checkedIn: contextCheckedIn,
    isCurrentEventLive: contextIsCurrentEventLive,
  } = useEventContext();
  const isUserCheckedIn = checkedIn ?? contextCheckedIn;
  const isEventLive = isCurrentEventLive ?? contextIsCurrentEventLive;
  const [songDetails, setSongDetails] = useState<Song>(() => normalizeSong(song as unknown as Record<string, unknown>));
  const [isLoadingDetails, setIsLoadingDetails] = useState(false);
  const [detailsError, setDetailsError] = useState<string | null>(null);
  const [isAddingToQueue, setIsAddingToQueue] = useState(false);
  const [isDeleting, setIsDeleting] = useState(false);
  const [isRequesting, setIsRequesting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [showEventSelectionModal, setShowEventSelectionModal] = useState(false);
  const [showJoinConfirmation, setShowJoinConfirmation] = useState(false);
  const [selectedEventId, setSelectedEventId] = useState<number | null>(null);

  useEffect(() => {
    setSongDetails(normalizeSong(song as unknown as Record<string, unknown>));
    setDetailsError(null);
  }, [song]);

  const validateToken = useCallback(() => {
    const token = localStorage.getItem("token");
    const userName = localStorage.getItem("userName");
    if (!token || !userName) {
      console.error("[SONG_DETAILS_MODAL] No token or userName found");
      setError("Authentication token or username missing. Please log in again.");
      navigate("/login");
      return null;
    }

    try {
      if (token.split('.').length !== 3) {
        console.error("[SONG_DETAILS_MODAL] Malformed token: does not contain three parts");
        localStorage.removeItem("token");
        localStorage.removeItem("userName");
        setError("Invalid token format. Please log in again.");
        navigate("/login");
        return null;
      }

      const payload = JSON.parse(atob(token.split('.')[1]));
      const exp = payload.exp * 1000;
      if (exp < Date.now()) {
        console.error("[SONG_DETAILS_MODAL] Token expired:", new Date(exp).toISOString());
        localStorage.removeItem("token");
        localStorage.removeItem("userName");
        setError("Session expired. Please log in again.");
        navigate("/login");
        return null;
      }
      console.log("[SONG_DETAILS_MODAL] Token validated:", { userName, exp: new Date(exp).toISOString() });
      return token;
    } catch (err) {
      console.error("[SONG_DETAILS_MODAL] Token validation error:", err);
      localStorage.removeItem("token");
      localStorage.removeItem("userName");
      setError("Invalid token. Please log in again.");
      navigate("/login");
      return null;
    }
  }, [navigate]);

  useEffect(() => {
    let isActive = true;

    const fetchSongDetails = async () => {
      if (!song.id) {
        return;
      }

      const token = validateToken();
      if (!token) {
        return;
      }

      setIsLoadingDetails(true);
      setDetailsError(null);

      try {
        const response = await fetch(`${API_ROUTES.SONG_BY_ID}/${song.id}`, {
          headers: { Authorization: `Bearer ${token}` },
        });
        const responseText = await response.text();

        if (!response.ok) {
          console.error('[SONG_DETAILS_MODAL] Failed to fetch song details:', response.status, responseText);
          if (isActive) {
            const message = response.status === 404 ? 'Song details not found.' : `Failed to load song details: ${response.status}`;
            setDetailsError(message);
          }
          return;
        }

        let parsed: unknown;
        try {
          parsed = JSON.parse(responseText);
        } catch (err) {
          console.error('[SONG_DETAILS_MODAL] Failed to parse song details JSON:', err, responseText);
          if (isActive) {
            setDetailsError('Received invalid song details from server.');
          }
          return;
        }

        if (isActive) {
          setSongDetails(prev => normalizeSong({ ...prev, ...(parsed as Record<string, unknown>) }));
        }
      } catch (err) {
        console.error('[SONG_DETAILS_MODAL] Error fetching song details:', err);
        if (isActive) {
          setDetailsError('Failed to load song details. Please try again.');
        }
      } finally {
        if (isActive) {
          setIsLoadingDetails(false);
        }
      }
    };

    fetchSongDetails();

    return () => {
      isActive = false;
    };
  }, [song.id, validateToken]);

  // Debug why Request Song button is not showing
  console.log('[SONG_DETAILS_MODAL] Rendering with props:', {
    songStatus: songDetails.status,
    hasOnRequestSong: !!onRequestSong,
    readOnly,
    isFavorite,
    isInQueue,
    eventId,
    checkedIn: isUserCheckedIn,
    isCurrentEventLive: isEventLive,
    normalizedSong: songDetails,
  });

  const formatBoolean = (value: boolean | null | undefined, trueLabel = 'Yes', falseLabel = 'No') => {
    if (value === undefined || value === null) {
      return 'Unknown';
    }
    return value ? trueLabel : falseLabel;
  };

  const formatGain = (value: number | null | undefined) => {
    if (value === undefined || value === null || Number.isNaN(value)) {
      return 'Unknown';
    }
    if (Math.abs(value) < 0.05) {
      return '0.0 dB';
    }
    return `${value >= 0 ? '+' : ''}${value.toFixed(1)} dB`;
  };

  const formatDuration = (value: number | null | undefined) => {
    if (value === undefined || value === null) {
      return 'None';
    }
    if (!Number.isFinite(value) || value <= 0) {
      return 'None';
    }
    const totalSeconds = Math.max(0, Math.round(value));
    const minutes = Math.floor(totalSeconds / 60);
    const seconds = totalSeconds % 60;
    return `${minutes}:${seconds.toString().padStart(2, '0')}`;
  };

  const handleAddToQueue = async (eventId: number) => {
    console.log("handleAddToQueue called with eventId:", eventId, "song:", songDetails, "onAddToQueue:", !!onAddToQueue);
    if (!onAddToQueue) {
      console.error("onAddToQueue is not defined");
      setError("Cannot add to queue: Functionality not available.");
      return;
    }
    if (!eventId) {
      console.error("Event ID is missing");
      setError("Please select an event to add the song to the queue.");
      return;
    }
    setIsAddingToQueue(true);
    setError(null);
    try {
      await onAddToQueue(songDetails, eventId);
      console.log("Song successfully added to queue for eventId:", eventId);
      setShowEventSelectionModal(false);
      setShowJoinConfirmation(false);
      onClose();
    } catch (err) {
      console.error("SongDetailsModal - Add to queue error:", err);
      const errorMessage = err instanceof Error ? err.message : "Failed to add song to queue. Please try again.";
      setError(errorMessage);
      if (errorMessage.includes("User not found")) {
        localStorage.clear();
        navigate("/login");
      }
    } finally {
      setIsAddingToQueue(false);
    }
  };

  const handleDeleteFromQueue = async () => {
    console.log("handleDeleteFromQueue called with eventId:", eventId, "queueId:", queueId, "onDeleteFromQueue:", !!onDeleteFromQueue);
    if (!onDeleteFromQueue || !eventId || !queueId) {
      console.error("Cannot delete from queue: Missing onDeleteFromQueue, eventId, or queueId");
      setError("Cannot delete from queue: Missing information.");
      return;
    }
    setIsDeleting(true);
    setError(null);
    try {
      await onDeleteFromQueue(eventId, queueId);
      console.log("Song successfully deleted from queue for eventId:", eventId, "queueId:", queueId);
      onClose();
    } catch (err) {
      console.error("SongDetailsModal - Delete from queue error:", err);
      const errorMessage = err instanceof Error ? err.message : "Failed to delete song from queue. Please try again.";
      setError(errorMessage);
      if (errorMessage.includes("User not found")) {
        localStorage.clear();
        navigate("/login");
      }
    } finally {
      setIsDeleting(false);
    }
  };

  const handleRequestSong = async () => {
    console.log("handleRequestSong called with song:", songDetails, "onRequestSong:", !!onRequestSong);
    if (!onRequestSong) {
      console.error("onRequestSong is not defined");
      setError("Cannot request song: Functionality not available.");
      return;
    }
    setIsRequesting(true);
    setError(null);
    try {
      await onRequestSong({
        id: songDetails.spotifyId || '0',
        title: songDetails.title,
        artist: songDetails.artist,
        genre: songDetails.genre || undefined,
        popularity: songDetails.popularity,
        bpm: songDetails.bpm,
        energy: songDetails.energy,
        valence: songDetails.valence,
        danceability: songDetails.danceability,
        decade: songDetails.decade || undefined,
      });
      console.log("Song successfully requested");
      onClose();
    } catch (err) {
      console.error("SongDetailsModal - Request song error:", err);
      const errorMessage = err instanceof Error ? err.message : "Failed to request song. Please try again.";
      setError(errorMessage);
      if (errorMessage.includes("User not found")) {
        localStorage.clear();
        navigate("/login");
      }
    } finally {
      setIsRequesting(false);
    }
  };

  const handleOpenEventSelection = () => {
    console.log("handleOpenEventSelection called");
    const userName = localStorage.getItem("userName");
    if (!userName) {
      console.error("UserName not found in localStorage");
      setError("User not found. Please log in again to add songs to the queue.");
      localStorage.clear();
      navigate("/login");
      return;
    }
    if (liveEvents.length > 0 && !isUserCheckedIn) {
      console.log("Live events exist, blocking event selection for non-checked-in user");
      setError("You must be checked into a live event to add to its queue.");
      return;
    }
    setShowEventSelectionModal(true);
  };

  const confirmJoinAndAdd = async () => {
    if (!selectedEventId || !onAddToQueue) return;
    const recentlyLeftEvent = localStorage.getItem("recentlyLeftEvent");
    const leftEventTimestamp = localStorage.getItem("recentlyLeftEventTimestamp");
    const now = Date.now();
    const threeMinutes = 3 * 60 * 1000;
    if (recentlyLeftEvent && leftEventTimestamp && selectedEventId.toString() === recentlyLeftEvent) {
      const timeSinceLeft = now - parseInt(leftEventTimestamp, 10);
      if (timeSinceLeft < threeMinutes) {
        console.log(`[CHECK_IN] Blocked: recently left event ${selectedEventId}, time since left: ${timeSinceLeft}ms`);
        setError("You recently left this event. Please wait 3 minutes before rejoining.");
        return;
      } else {
        console.log("[CHECK_IN] Clearing expired recentlyLeftEvent");
        localStorage.removeItem("recentlyLeftEvent");
        localStorage.removeItem("recentlyLeftEventTimestamp");
      }
    }
    const token = validateToken();
    if (!token) return;
    try {
      const requestorId = localStorage.getItem("userName") || "unknown";
      const requestData: AttendanceAction = { RequestorId: requestorId };
      console.log(`Check-in Request for event ${selectedEventId}:`, requestData);
      const response = await fetch(`${API_ROUTES.EVENTS}/${selectedEventId}/attendance/check-in`, {
        method: 'POST',
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(requestData),
      });
      const responseText = await response.text();
      console.log(`Check-in Response for event ${selectedEventId}:`, response.status, responseText);
      if (!response.ok) {
        if (response.status === 401) {
          setError("Session expired. Please log in again.");
          localStorage.clear();
          navigate("/login");
          return;
        }
        throw new Error(`Check-in failed: ${response.status} - ${responseText}`);
      }
      const event = liveEvents.find(e => e.eventId === selectedEventId) || upcomingEvents.find(e => e.eventId === selectedEventId) || currentEvent;
      if (event) {
        setCurrentEvent(event);
        setCheckedIn(true);
        setIsCurrentEventLive(event.status.toLowerCase() === "live");
      }
      await handleAddToQueue(selectedEventId);
    } catch (err) {
      console.error("Join and add error:", err);
      setError(err instanceof Error ? err.message : "Failed to join event and add song.");
    }
  };

  return (
    <>
      <div className="modal-overlay song-details-modal mobile-song-details">
        <div className="modal-content song-details-modal">
          <div className="modal-body">
            <div className="song-info">
              <div className="song-title">{songDetails.title}</div>
              <div className="song-artist">({songDetails.artist || 'Unknown Artist'})</div>
              {songDetails.status && (
                <div className="song-status">
                  {songDetails.status.toLowerCase() === 'active' && (
                    <span className="song-status-badge available">Available</span>
                  )}
                  {songDetails.status.toLowerCase() === 'pending' && (
                    <span className="song-status-badge pending">Pending</span>
                  )}
                  {songDetails.status.toLowerCase() === 'unavailable' && (
                    <span className="song-status-badge unavailable">Unavailable</span>
                  )}
                </div>
              )}
            </div>
            <div className="song-details">
              <p className="modal-text"><strong>Song ID:</strong> {songDetails.id}</p>
              <p className="modal-text"><strong>Genre:</strong> {songDetails.genre ?? 'Unknown'}</p>
              <p className="modal-text"><strong>Status:</strong> {songDetails.status ?? 'Unknown'}</p>
              <p className="modal-text"><strong>Mood:</strong> {songDetails.mood ?? 'Unknown'}</p>
              <p className="modal-text"><strong>Server Cached:</strong> {formatBoolean(songDetails.serverCached ?? songDetails.cached)}</p>
              <p className="modal-text"><strong>Mature Content:</strong> {formatBoolean(songDetails.mature)}</p>
              <p className="modal-text"><strong>Gain Value:</strong> {formatGain(songDetails.normalizationGain)}</p>
              <p className="modal-text"><strong>FO Start:</strong> {formatDuration(songDetails.fadeStartTime)}</p>
              <p className="modal-text"><strong>Intro Mute:</strong> {formatDuration(songDetails.introMuteDuration)}</p>
              {songDetails.youTubeUrl && (
                <p className="modal-text">
                  <strong>Song URL:</strong>{' '}
                  <a href={songDetails.youTubeUrl} target="_blank" rel="noopener noreferrer">
                    {songDetails.youTubeUrl}
                  </a>
                </p>
              )}
              {typeof songDetails.popularity === 'number' && songDetails.popularity > 0 && (
                <p className="modal-text"><strong>Popularity:</strong> {songDetails.popularity}</p>
              )}
              {typeof songDetails.bpm === 'number' && songDetails.bpm > 0 && (
                <p className="modal-text"><strong>BPM:</strong> {songDetails.bpm}</p>
              )}
              {typeof songDetails.energy === 'number' && songDetails.energy > 0 && (
                <p className="modal-text"><strong>Energy:</strong> {songDetails.energy}</p>
              )}
              {typeof songDetails.valence === 'number' && songDetails.valence > 0 && (
                <p className="modal-text"><strong>Valence:</strong> {songDetails.valence}</p>
              )}
              {typeof songDetails.danceability === 'number' && songDetails.danceability > 0 && (
                <p className="modal-text"><strong>Danceability:</strong> {songDetails.danceability}</p>
              )}
              {songDetails.decade && <p className="modal-text"><strong>Decade:</strong> {songDetails.decade}</p>}
            </div>
            {isLoadingDetails && <p className="modal-text">Loading song details...</p>}
            {detailsError && <p className="modal-error">{detailsError}</p>}
            {error && <p className="modal-error">{error}</p>}
            {!readOnly && (
              <div className="song-actions">
                {onToggleFavorite && (
                  <button
                    onClick={() => {
                      console.log("Toggle favorite button clicked for song:", songDetails);
                      onToggleFavorite(songDetails);
                    }}
                    onTouchEnd={() => {
                      console.log("Toggle favorite button touched for song:", songDetails);
                      onToggleFavorite(songDetails);
                    }}
                    className="action-button"
                  >
                    {isFavorite ? "Remove from Favorites" : "Add to Favorites"}
                  </button>
                )}
                {isInQueue && onDeleteFromQueue && eventId && queueId ? (
                  <button
                    onClick={() => {
                      console.log("Remove from Queue button clicked");
                      handleDeleteFromQueue();
                    }}
                    onTouchEnd={() => {
                      console.log("Remove from Queue button touched");
                      handleDeleteFromQueue();
                    }}
                    className="action-button"
                    disabled={isDeleting}
                  >
                    {isDeleting ? "Deleting..." : "Remove from Queue"}
                  </button>
                ) : (
                  isUserCheckedIn && isEventLive && onAddToQueue && (currentEvent || eventId) && (
                    <button
                      onClick={() => {
                        const targetEventId = eventId ?? currentEvent?.eventId;
                        console.log("Add to Queue button clicked with event:", targetEventId);
                        if (targetEventId) handleAddToQueue(targetEventId);
                      }}
                      onTouchEnd={() => {
                        const targetEventId = eventId ?? currentEvent?.eventId;
                        console.log("Add to Queue button touched with event:", targetEventId);
                        if (targetEventId) handleAddToQueue(targetEventId);
                      }}
                      className="action-button"
                      disabled={isAddingToQueue || isInQueue}
                    >
                      {isAddingToQueue
                        ? "Adding..."
                        : `Add to Queue${currentEvent?.eventCode ? `: ${currentEvent.eventCode}` : ""}`}
                    </button>
                  )
                )}
                {!isUserCheckedIn && !isEventLive && onAddToQueue && (
                  <button
                    onClick={() => {
                      console.log("Add to Queue (pre-select) button clicked");
                      handleOpenEventSelection();
                    }}
                    onTouchEnd={() => {
                      console.log("Add to Queue (pre-select) button touched");
                      handleOpenEventSelection();
                    }}
                    className="action-button"
                    disabled={isAddingToQueue || upcomingEvents.length === 0 || isInQueue}
                  >
                    {isAddingToQueue ? "Adding..." : "Add to Queue"}
                  </button>
                )}
                {onRequestSong && !songDetails.status && (
                  <button
                    onClick={() => {
                      console.log("Request Song button clicked for song:", songDetails);
                      handleRequestSong();
                    }}
                    onTouchEnd={() => {
                      console.log("Request Song button touched for song:", songDetails);
                      handleRequestSong();
                    }}
                    className="action-button"
                    disabled={isRequesting}
                  >
                    {isRequesting ? "Requesting..." : "Request Song"}
                  </button>
                )}
              </div>
            )}
          </div>
          <div className="modal-footer">
            <button
              onClick={onClose}
              onTouchEnd={onClose}
              className="action-button"
            >
              Done
            </button>
          </div>
        </div>
      </div>
      {showEventSelectionModal && !readOnly && (
        <div className="modal-overlay secondary-modal song-details-modal mobile-song-details">
          <div className="modal-content song-details-modal">
            <h3 className="modal-title">Select Event Queue</h3>
            {error && <p className="modal-error">{error}</p>}
            <div className="event-list">
              {upcomingEvents
                .filter(event => event.status.toLowerCase() === "upcoming")
                .map(event => (
                  <div
                    key={event.eventId}
                    className="event-item"
                    onClick={() => {
                      console.log("Event selected for adding to queue:", event);
                      handleAddToQueue(event.eventId);
                    }}
                    onTouchEnd={() => {
                      console.log("Event selected for adding to queue (touch):", event);
                      handleAddToQueue(event.eventId);
                    }}
                  >
                    {event.status}: {event.eventCode} ({event.scheduledDate})
                  </div>
                ))}
            </div>
            <div className="modal-footer">
              <button
                onClick={() => {
                  console.log("Cancel event selection modal");
                  setShowEventSelectionModal(false);
                }}
                onTouchEnd={() => {
                  console.log("Cancel event selection modal (touch)");
                  setShowEventSelectionModal(false);
                }}
                className="action-button"
              >
                Cancel
              </button>
            </div>
          </div>
        </div>
      )}
      {showJoinConfirmation && selectedEventId && (
        <div className="modal-overlay secondary-modal song-details-modal mobile-song-details">
          <div className="modal-content song-details-modal">
            <h3 className="modal-title">Join Event</h3>
            <p>Do you want to join the event and add this song to the queue?</p>
            {error && <p className="modal-error">{error}</p>}
            <div className="modal-footer">
              <button
                onClick={() => {
                  console.log("Confirm join and add for eventId:", selectedEventId);
                  confirmJoinAndAdd();
                }}
                onTouchEnd={() => {
                  console.log("Confirm join and add (touch) for eventId:", selectedEventId);
                  confirmJoinAndAdd();
                }}
                className="action-button"
                disabled={isAddingToQueue}
              >
                {isAddingToQueue ? "Joining..." : "Join and Add"}
              </button>
              <button
                onClick={() => {
                  console.log("Cancel join confirmation");
                  setShowJoinConfirmation(false);
                  setSelectedEventId(null);
                }}
                onTouchEnd={() => {
                  console.log("Cancel join confirmation (touch)");
                  setShowJoinConfirmation(false);
                  setSelectedEventId(null);
                }}
                className="action-button"
              >
                Cancel
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
};

export default SongDetailsModal;