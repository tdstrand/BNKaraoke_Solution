// src/components/SongDetailsModal.tsx
import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import './SongDetailsModal.css';
import { Song, Event, AttendanceAction, SpotifySong } from '../types';
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
  checkedIn: boolean;
  isCurrentEventLive: boolean;
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
  const { currentEvent, setCurrentEvent, setCheckedIn, setIsCurrentEventLive, liveEvents, upcomingEvents } = useEventContext();
  const [isAddingToQueue, setIsAddingToQueue] = useState(false);
  const [isDeleting, setIsDeleting] = useState(false);
  const [isRequesting, setIsRequesting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [showEventSelectionModal, setShowEventSelectionModal] = useState(false);
  const [showJoinConfirmation, setShowJoinConfirmation] = useState(false);
  const [selectedEventId, setSelectedEventId] = useState<number | null>(null);

  // Debug why Request Song button is not showing
  console.log('[SONG_DETAILS_MODAL] Rendering with props:', {
    songStatus: song.status,
    hasOnRequestSong: !!onRequestSong,
    readOnly,
    isFavorite,
    isInQueue,
    eventId,
    checkedIn,
    isCurrentEventLive,
  });

  const validateToken = () => {
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
  };

  const handleAddToQueue = async (eventId: number) => {
    console.log("handleAddToQueue called with eventId:", eventId, "song:", song, "onAddToQueue:", !!onAddToQueue);
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
      await onAddToQueue(song, eventId);
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
    console.log("handleRequestSong called with song:", song, "onRequestSong:", !!onRequestSong);
    if (!onRequestSong) {
      console.error("onRequestSong is not defined");
      setError("Cannot request song: Functionality not available.");
      return;
    }
    setIsRequesting(true);
    setError(null);
    try {
      await onRequestSong({
        id: song.spotifyId || '0',
        title: song.title,
        artist: song.artist,
        genre: song.genre,
        popularity: song.popularity,
        bpm: song.bpm,
        energy: song.energy,
        valence: song.valence,
        danceability: song.danceability,
        decade: song.decade,
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
    if (liveEvents.length > 0 && !checkedIn) {
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
          <div className="song-info">
            <div className="song-title">{song.title}</div>
            <div className="song-artist">({song.artist || 'Unknown Artist'})</div>
            {song.status && (
              <div className="song-status">
                {song.status.toLowerCase() === 'active' && (
                  <span className="song-status-badge available">Available</span>
                )}
                {song.status.toLowerCase() === 'pending' && (
                  <span className="song-status-badge pending">Pending</span>
                )}
                {song.status.toLowerCase() === 'unavailable' && (
                  <span className="song-status-badge unavailable">Unavailable</span>
                )}
              </div>
            )}
          </div>
          <div className="song-details">
            {song.genre && <p className="modal-text"><strong>Genre:</strong> {song.genre}</p>}
            {typeof song.popularity === 'number' && song.popularity > 0 && (
              <p className="modal-text"><strong>Popularity:</strong> {song.popularity}</p>
            )}
            {typeof song.bpm === 'number' && song.bpm > 0 && (
              <p className="modal-text"><strong>BPM:</strong> {song.bpm}</p>
            )}
            {typeof song.energy === 'number' && song.energy > 0 && (
              <p className="modal-text"><strong>Energy:</strong> {song.energy}</p>
            )}
            {typeof song.valence === 'number' && song.valence > 0 && (
              <p className="modal-text"><strong>Valence:</strong> {song.valence}</p>
            )}
            {typeof song.danceability === 'number' && song.danceability > 0 && (
              <p className="modal-text"><strong>Danceability:</strong> {song.danceability}</p>
            )}
            {song.decade && <p className="modal-text"><strong>Decade:</strong> {song.decade}</p>}
          </div>
          {error && <p className="modal-error">{error}</p>}
          {!readOnly && (
            <div className="song-actions">
              {onToggleFavorite && (
                <button
                  onClick={() => {
                    console.log("Toggle favorite button clicked for song:", song);
                    onToggleFavorite(song);
                  }}}
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
                  }}}
                  className="action-button"
                  disabled={isDeleting}
                >
                  {isDeleting ? "Deleting..." : "Remove from Queue"}
                </button>
              ) : (
                checkedIn && isCurrentEventLive && onAddToQueue && currentEvent && (
                  <button
                    onClick={() => {
                      console.log("Add to Queue button clicked with currentEvent:", currentEvent);
                      handleAddToQueue(currentEvent.eventId);
                    }}}
                    className="action-button"
                    disabled={isAddingToQueue || isInQueue}
                  >
                    {isAddingToQueue ? "Adding..." : `Add to Queue: ${currentEvent.eventCode}`}
                  </button>
                )
              )}
              {!checkedIn && !isCurrentEventLive && onAddToQueue && (
                <button
                  onClick={() => {
                    console.log("Add to Queue (pre-select) button clicked");
                    handleOpenEventSelection();
                  }}}
                  className="action-button"
                  disabled={isAddingToQueue || upcomingEvents.length === 0 || isInQueue}
                >
                  {isAddingToQueue ? "Adding..." : "Add to Queue"}
                </button>
              )}
              {onRequestSong && !song.status && (
                <button
                  onClick={() => {
                    console.log("Request Song button clicked for song:", song);
                    handleRequestSong();
                  }}}
                  className="action-button"
                  disabled={isRequesting}
                >
                  {isRequesting ? "Requesting..." : "Request Song"}
                </button>
              )}
            </div>
          )}
          <div className="modal-footer">
            <button
              onClick={onClose}
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
                    }}}
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
                }}}
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
                }}}
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
                }}}
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